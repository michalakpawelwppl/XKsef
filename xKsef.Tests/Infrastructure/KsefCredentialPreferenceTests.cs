using System.Data.Common;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using xKsef.Api.Contracts;
using xKsef.Api.Controllers;
using xKsef.Application.Credentials;
using xKsef.Application.Tenants;
using xKsef.Contracts.Tenants;
using xKsef.CredentialService.Services;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Ksef.Authentication;

namespace xKsef.Tests.Infrastructure;

public sealed class KsefCredentialPreferenceTests
{
    private const string User = "owner";
    private const string Nip = "0123456789";

    [Fact]
    public async Task CertificateListIsOwnerScopedActiveAndDoesNotReadSecrets()
    {
        using var store = new KsefSessionTestStore();
        var sql = new ReadCommands();
        await using var db = store.CreateContext(sql);
        var service = store.CreateService(db);
        Guid own = await service.SaveAsync(null, User, "Certificate", [1, 2, 3]);
        Guid inactive = await service.SaveAsync(null, User, "Certificate", [4]);
        await service.SaveAsync(null, "other-owner", "Certificate", [5]);
        await service.SaveAsync(Guid.NewGuid(), User, "Token", [6], contextNip: Nip);
        await db.KsefCredentials.Where(c => c.Id == inactive).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
        await db.KsefCredentials.Where(c => c.Id == own).ExecuteUpdateAsync(s => s.SetProperty(c => c.Name, "My certificate"));
        sql.Commands.Clear();
        var list = await service.GetCertificatesAsync(User);
        Assert.Equal(own, Assert.Single(list).CredentialId);
        Assert.Equal("My certificate", list[0].Name);
        Assert.Equal(new[] { "CreatedAt", "CredentialId", "Name" }, list[0].GetType().GetProperties().Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal));
        Assert.All(sql.Commands, command =>
        {
            Assert.DoesNotContain("EncryptedData", command);
            Assert.DoesNotContain("EncryptedDek", command);
            Assert.DoesNotContain("Nonce", command);
            Assert.DoesNotContain("AuthTag", command);
        });
        Assert.NotEmpty(sql.Commands);
    }

    [Fact]
    public async Task OneCertificateCanBePreferredForTwoTenantsAndAuthAndImportUseItsExactId()
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid selected = await service.SaveAsync(null, User, "Certificate", [1]);
        Guid newer = await service.SaveAsync(null, User, "Certificate", [2]);
        var tenants = new Tenants();
        var client = new Bridge(service);
        var controller = new TenantsController(tenants, client);
        Authorize(controller);
        foreach (Guid tenant in tenants.Active)
            Assert.IsType<NoContentResult>(await controller.SetPreferredKsefCredentialAsync(tenant, new(selected), default));
        Assert.All(tenants.Preferences.Values, id => Assert.Equal(selected, id));
        foreach (Guid tenant in tenants.Active)
        {
            var auth = new KsefAuthenticationController(null!, tenants, null!, null!, client);
            Authorize(auth, tenant);
            Assert.IsType<OkObjectResult>(await auth.AuthenticateCertificateAsync(new(selected), default));
            Assert.Equal("access", await new KsefTokenProvider(client, tenants).GetAccessTokenAsync(User, tenant));
            Assert.IsType<ConflictObjectResult>(await auth.AuthenticateCertificateAsync(new(newer), default));
        }
        Assert.Equal(6, client.Used.Count);
        Assert.All(client.Used, operation => Assert.Equal(selected, operation.Id));
        Assert.Equal(2, client.Used.Select(o => o.Tenant).Distinct().Count());
    }

    [Theory]
    [InlineData("foreign")]
    [InlineData("inactive")]
    [InlineData("token-tenant")]
    [InlineData("token-nip")]
    [InlineData("not-member")]
    [InlineData("inactive-tenant")]
    public async Task InvalidPreferenceIsRejectedWithoutChangingPreviousSelection(string reason)
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        var tenants = new Tenants();
        Guid tenant = tenants.Active.First();
        Guid previous = Guid.NewGuid();
        tenants.Preferences[tenant] = previous;
        Guid id;
        if (reason.StartsWith("token", StringComparison.Ordinal))
            id = await service.SaveAsync(reason == "token-tenant" ? Guid.NewGuid() : tenant, User, "Token", [1], contextNip: reason == "token-nip" ? "9876543210" : Nip);
        else
            id = await service.SaveAsync(null, reason == "foreign" ? "another" : User, "Certificate", [1]);
        if (reason == "inactive") await db.KsefCredentials.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
        if (reason is "not-member" or "inactive-tenant") tenants.Active.Remove(tenant);
        var controller = new TenantsController(tenants, new Bridge(service));
        Authorize(controller);
        var result = await controller.SetPreferredKsefCredentialAsync(tenant, new(id), default);
        if (reason is "not-member" or "inactive-tenant") Assert.IsType<ForbidResult>(result);
        else Assert.IsType<BadRequestObjectResult>(result);
        Assert.Equal(previous, tenants.Preferences[tenant]);
        Assert.Equal(0, tenants.Writes);
    }

    [Theory]
    [InlineData("Certificate")]
    [InlineData("Token")]
    public async Task AuthenticationRequiresSavedPreferenceAndRejectsInvalidSourceWithoutFallback(string type)
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        var tenants = new Tenants();
        Guid tenant = tenants.Active.First();
        Guid id = await service.SaveAsync(type == "Token" ? tenant : null, User, type, [1], contextNip: type == "Token" ? Nip : null);
        var client = new Bridge(service);
        var auth = new KsefAuthenticationController(null!, tenants, null!, null!, client);
        Authorize(auth, tenant);
        Task<IActionResult> Call() => type == "Token" ? auth.AuthenticateTokenAsync(new(id), default) : auth.AuthenticateCertificateAsync(new(id), default);
        Assert.IsType<BadRequestObjectResult>(await Call());
        tenants.Preferences[tenant] = id;
        Assert.IsType<OkObjectResult>(await Call());
        client.Used.Clear();
        await db.KsefCredentials.Where(c => c.Id == id).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
        Assert.IsType<BadRequestObjectResult>(await Call());
        // Even another valid credential cannot be selected in place of the invalid preference.
        await service.SaveAsync(type == "Token" ? tenant : null, User, type, [2], contextNip: type == "Token" ? Nip : null);
        Assert.IsType<BadRequestObjectResult>(await Call());
        Assert.Empty(client.Used);
    }

    [Fact]
    public void ExistingAuthorizationFiltersRemainOnCertificateManagementAndAuthentication()
    {
        var management = typeof(KsefCredentialsController).GetCustomAttributes(typeof(ServiceFilterAttribute), true).Cast<ServiceFilterAttribute>();
        Assert.Contains(management, a => a.ServiceType == typeof(xKsef.Api.Authorization.CredentialManagementAuthorizationFilter));
        var auth = typeof(KsefAuthenticationController).GetCustomAttributes(typeof(ServiceFilterAttribute), true).Cast<ServiceFilterAttribute>();
        Assert.Contains(auth, a => a.ServiceType == typeof(xKsef.Api.Authorization.TenantAuthorizationFilter));
    }

    private static void Authorize(ControllerBase controller, Guid? tenant = null)
    {
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, User) }, "JWT-test")) } };
        if (tenant.HasValue) controller.HttpContext.Items["TenantId"] = tenant.Value;
    }

    private sealed class ReadCommands : DbCommandInterceptor
    {
        public List<string> Commands { get; } = [];
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class Tenants : ITenantService
    {
        public HashSet<Guid> Active { get; } = [Guid.NewGuid(), Guid.NewGuid()];
        public Dictionary<Guid, Guid?> Preferences { get; } = [];
        public int Writes { get; private set; }
        public Task<IReadOnlyList<TenantDto>> GetUserTenantsAsync(string userId, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<TenantDto>>(userId == User ? Active.Select(t => new TenantDto(t, "Tenant", "Admin")).ToArray() : []);
        public Task<TaxEntity?> GetTaxEntityAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<TaxEntity?>(Active.Contains(tenantId) ? new() { Nip = Nip } : null);
        public Task<Guid?> GetPreferredKsefCredentialIdAsync(Guid tenantId, string userId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(User, userId);
            return Task.FromResult(Preferences.GetValueOrDefault(tenantId));
        }
        public Task SetPreferredKsefCredentialIdAsync(Guid tenantId, string userId, Guid? credentialId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(User, userId);
            Assert.Contains(tenantId, Active);
            Writes++;
            Preferences[tenantId] = credentialId;
            return Task.CompletedTask;
        }
        public Task<CreateTenantResult> CreateAsync(string name, string nip, string taxEntityName, string userLogin, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }

    private sealed class Bridge(KsefCredentialService service) : ICredentialServiceClient
    {
        public List<(Guid Id, Guid Tenant, string Kind)> Used { get; } = [];
        public Task<string?> GetCredentialTypeAsync(Guid id, Guid tenant, string user, string nip, CancellationToken cancellationToken = default) => service.GetCredentialTypeAsync(id, tenant, user, nip, cancellationToken);
        public Task AuthenticateCertificateAsync(Guid id, Guid tenant, string user, string nip, CancellationToken cancellationToken = default) { Used.Add((id, tenant, "Certificate")); return Task.CompletedTask; }
        public Task AuthenticateTokenAsync(Guid id, Guid tenant, string user, string nip, CancellationToken cancellationToken = default) { Used.Add((id, tenant, "Token")); return Task.CompletedTask; }
        public Task EnsureAuthenticatedAsync(Guid id, Guid tenant, string user, string nip, CancellationToken cancellationToken = default) { Used.Add((id, tenant, "Ensure")); return Task.CompletedTask; }
        public Task<KsefAccessTokenResponse> GetAccessTokenAsync(Guid id, Guid tenant, string user, string nip, CancellationToken cancellationToken = default) { Used.Add((id, tenant, "Access")); return Task.FromResult(new KsefAccessTokenResponse("access", DateTimeOffset.UtcNow.AddHours(1))); }
        public Task<KsefTokenInfo> GetCertificateInfoAsync(string user, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Automatic certificate selection is forbidden.");
        public Task<KsefTokenInfo> GetTokenInfoAsync(Guid tenant, string user, string nip, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Automatic token selection is forbidden.");
        public Task<Guid> SaveCertificateAsync(string user, string cert, string key, string password, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid> SaveTokenAsync(Guid tenant, string user, string token, string nip, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<KsefCertificateInfo>> GetCertificatesAsync(string user, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
