using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using xKsef.Application.Credentials;
using xKsef.Application.Tenants;
using xKsef.Contracts.Tenants;
using xKsef.CredentialService.Data;
using xKsef.CredentialService.Security;
using xKsef.CredentialService.Services;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Credentials;
using ApiController = xKsef.Api.Controllers.KsefCredentialsController;
using InternalController = xKsef.CredentialService.Controllers.KsefCredentialsController;

namespace xKsef.Tests.Infrastructure;

public sealed class KsefTokenContextTests
{
    private const string Nip = "0123456789";
    private const string OtherNip = "9876543210";
    private const string UserId = "owner";

    [Fact]
    public async Task SourceTokenPersistsContextAndUsesV2Encryption()
    {
        await using var fixture = new Fixture();
        Guid id = await fixture.Service.SaveAsync(fixture.TenantId, UserId, "Token", [1, 2, 3], contextNip: Nip);
        var stored = await fixture.Db.KsefCredentials.SingleAsync();
        Assert.Equal(Nip, stored.ContextNip);
        Assert.Equal(fixture.TenantId, stored.TenantId);
        Assert.Equal(UserId, stored.UserId);
        Assert.Equal(new byte[] { 1, 2, 3 }, fixture.Crypto.Decrypt(Envelope(stored), SecretEncryptionContext.KsefToken(id, UserId, fixture.TenantId, Nip)));
        Assert.Equal(new byte[] { 1, 2, 3 }, await fixture.Service.GetAsync(id, fixture.TenantId, UserId, "Token", contextNip: Nip));
        Assert.Equal(id, await fixture.Service.FindCredentialIdAsync(fixture.TenantId, UserId, "Token", contextNip: Nip));
        Assert.Null(await fixture.Service.GetAsync(id, fixture.TenantId, "another-user", "Token", contextNip: Nip));
        Assert.Null(await fixture.Service.GetAsync(id, Guid.NewGuid(), UserId, "Token", contextNip: Nip));
        Assert.Null(await fixture.Service.GetAsync(id, fixture.TenantId, UserId, "Token", contextNip: OtherNip));
        Assert.Null(await fixture.Service.FindCredentialIdAsync(fixture.TenantId, "another-user", "Token", contextNip: Nip));
        Assert.Null(await fixture.Service.FindCredentialIdAsync(Guid.NewGuid(), UserId, "Token", contextNip: Nip));
        Assert.Null(await fixture.Service.FindCredentialIdAsync(fixture.TenantId, UserId, "Token", contextNip: OtherNip));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("123456789")]
    [InlineData("12345678901")]
    [InlineData("012345678 ")]
    [InlineData("１２３４５６７８９０")]
    [InlineData("٠١٢٣٤٥٦٧٨٩")]
    public async Task InvalidRequestContextIsRejectedBeforeSourceOrSessionAccess(string? nip)
    {
        await using var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveAsync(fixture.TenantId, UserId, "Token", [1], contextNip: nip));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.GetAsync(Guid.NewGuid(), fixture.TenantId, UserId, "Token", contextNip: nip));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.FindCredentialIdAsync(fixture.TenantId, UserId, "Token", contextNip: nip));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.HasValidSessionAsync(fixture.TenantId, UserId, Guid.NewGuid(), nip!));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.GetRefreshTokenAsync(fixture.TenantId, UserId, Guid.NewGuid(), nip!));
        Assert.Empty(fixture.Db.ChangeTracker.Entries());
        var controller = new InternalController(fixture.Service, null!, null!, null!, null!);
        Assert.IsType<BadRequestObjectResult>(await controller.SaveTokenAsync(new() { TenantId = fixture.TenantId, UserId = UserId, Token = "secret", ContextNip = nip! }, default));
        Assert.IsType<BadRequestObjectResult>(await controller.GetTokenInfoAsync(fixture.TenantId, UserId, nip!, default));
        Assert.Empty(await fixture.Db.KsefCredentials.ToListAsync());
    }

    [Theory]
    [InlineData("AccessToken")]
    [InlineData("RefreshToken")]
    [InlineData("Unknown")]
    public async Task GenericSecretOperationsRejectSessionTypes(string type)
    {
        await using var fixture = new Fixture();
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.SaveAsync(fixture.TenantId, UserId, type, [1], contextNip: Nip));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.GetAsync(Guid.NewGuid(), fixture.TenantId, UserId, type, contextNip: Nip));
        Assert.Empty(await fixture.Db.KsefCredentials.ToListAsync());
    }

    [Fact]
    public async Task CertificateHasNoTenantOrNipAndKeepsOwnerScope()
    {
        await using var fixture = new Fixture();
        Guid id = await fixture.Service.SaveAsync(null, UserId, "Certificate", [5]);
        var stored = await fixture.Db.KsefCredentials.SingleAsync();
        Assert.Null(stored.ContextNip);
        Assert.Equal(new byte[] { 5 }, await fixture.Service.GetAsync(id, null, UserId, "Certificate"));
        Assert.Equal(id, await fixture.Service.FindCredentialIdAsync(null, UserId, "Certificate"));
        await Assert.ThrowsAsync<ArgumentException>(() => fixture.Service.GetAsync(id, Guid.NewGuid(), UserId, "Certificate"));
        Assert.Null(await fixture.Service.GetAsync(id, null, "another", "Certificate"));
        await fixture.Service.SaveSessionTokensAsync(fixture.TenantId, UserId, id, Nip, "access", fixture.ValidUntil, "refresh", fixture.ValidUntil);
        Assert.True(await fixture.Service.HasValidSessionAsync(fixture.TenantId, UserId, id, Nip));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(OtherNip)]
    [InlineData("１２３４５６７８９０")]
    public async Task MissingOrWrongStoredContextCannotReadSecretOrReuseOrRefreshValidSession(string? sourceNip)
    {
        await using var fixture = new Fixture();
        var source = await fixture.SeedSource(sourceNip);
        await fixture.SeedSession(source.Id);
        Assert.Null(await fixture.Service.GetAsync(source.Id, fixture.TenantId, UserId, "Token", contextNip: Nip));
        Assert.Null(await fixture.Service.FindCredentialIdAsync(fixture.TenantId, UserId, "Token", contextNip: Nip));
        Assert.False(await fixture.Service.HasValidSessionAsync(fixture.TenantId, UserId, source.Id, Nip));
        Assert.Null(await fixture.Service.GetAccessTokenAsync(fixture.TenantId, UserId, source.Id, Nip));
        Assert.Null(await fixture.Service.GetRefreshTokenAsync(fixture.TenantId, UserId, source.Id, Nip));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveAccessTokenAsync(fixture.TenantId, UserId, source.Id, Nip, "replacement", fixture.ValidUntil));
        await Assert.ThrowsAsync<InvalidOperationException>(() => fixture.Service.SaveSessionTokensAsync(fixture.TenantId, UserId, source.Id, Nip, "new-access", fixture.ValidUntil, "new-refresh", fixture.ValidUntil));
        // A null SDK dependency makes any attempt to refresh externally fail this test.
        Assert.False(await new KsefTokenRefreshService(fixture.Service, null!).TryRefreshAccessTokenAsync(fixture.TenantId, UserId, source.Id, Nip));
        var tokenAuthentication = new KsefTokenAuthenticationService(fixture.Service, null!, null!, new KsefAuthenticationLock(), null!);
        await Assert.ThrowsAsync<InvalidOperationException>(() => tokenAuthentication.AuthenticateAsync(source.Id, fixture.TenantId, UserId, Nip));
        await Assert.ThrowsAsync<InvalidOperationException>(() => tokenAuthentication.AuthenticateCoreAsync(source.Id, fixture.TenantId, UserId, Nip));
        var sessionService = new KsefSessionService(fixture.Service, null!, tokenAuthentication, new KsefAuthenticationLock(), new KsefTokenRefreshService(fixture.Service, null!));
        await Assert.ThrowsAsync<InvalidOperationException>(() => sessionService.EnsureAuthenticatedAsync(source.Id, fixture.TenantId, UserId, Nip));
        Assert.Single(await fixture.Db.KsefCredentials.ToListAsync());
        Assert.Equal(sourceNip, source.ContextNip);
        Assert.Single(await fixture.Db.KsefSessions.ToListAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SessionCannotAuthorizeOtherTenantOrUser(bool changeTenant)
    {
        await using var fixture = new Fixture();
        var source = await fixture.SeedSource(Nip);
        Guid requestedTenant = changeTenant ? Guid.NewGuid() : fixture.TenantId;
        string requestedUser = changeTenant ? UserId : "intruder";
        await fixture.SeedSession(source.Id, requestedTenant);
        Assert.False(await fixture.Service.HasValidSessionAsync(requestedTenant, requestedUser, source.Id, Nip));
        Assert.Null(await fixture.Service.GetAccessTokenAsync(requestedTenant, requestedUser, source.Id, Nip));
        Assert.Null(await fixture.Service.GetRefreshTokenAsync(requestedTenant, requestedUser, source.Id, Nip));
        Assert.False(await new KsefTokenRefreshService(fixture.Service, null!).TryRefreshAccessTokenAsync(requestedTenant, requestedUser, source.Id, Nip));
    }

    [Fact]
    public async Task MatchingContextPreservesDedicatedSessionStorageAndRefreshToken()
    {
        await using var fixture = new Fixture();
        var source = await fixture.SeedSource(Nip);
        await fixture.Service.SaveSessionTokensAsync(fixture.TenantId, UserId, source.Id, Nip, "access", fixture.ValidUntil, "refresh", fixture.ValidUntil);
        Assert.True(await fixture.Service.HasValidSessionAsync(fixture.TenantId, UserId, source.Id, Nip));
        Assert.Equal("access", Encoding.UTF8.GetString((await fixture.Service.GetAccessTokenAsync(fixture.TenantId, UserId, source.Id, Nip))!.TokenBytes));
        await fixture.Service.SaveAccessTokenAsync(fixture.TenantId, UserId, source.Id, Nip, "new-access", fixture.ValidUntil.AddHours(1));
        Assert.Equal("new-access", Encoding.UTF8.GetString((await fixture.Service.GetAccessTokenAsync(fixture.TenantId, UserId, source.Id, Nip))!.TokenBytes));
        Assert.Equal("refresh", Encoding.UTF8.GetString((await fixture.Service.GetRefreshTokenAsync(fixture.TenantId, UserId, source.Id, Nip))!.TokenBytes));
        Assert.Single(await fixture.Db.KsefCredentials.ToListAsync());
        Assert.Single(await fixture.Db.KsefSessions.ToListAsync());
        Assert.Null(await fixture.Service.GetAccessTokenAsync(fixture.TenantId, UserId, source.Id, OtherNip));
    }

    [Theory]
    [InlineData(null)]
    [InlineData(OtherNip)]
    public async Task LookupDoesNotFallBackToOlderMatchingToken(string? latestNip)
    {
        await using var fixture = new Fixture();
        var older = await fixture.SeedSource(Nip);
        older.CreatedAt = DateTimeOffset.UtcNow.AddDays(-1);
        await fixture.Db.SaveChangesAsync();
        await fixture.SeedSource(latestNip);
        Assert.Null(await fixture.Service.FindCredentialIdAsync(fixture.TenantId, UserId, "Token", contextNip: Nip));
        Assert.Equal(new byte[] { 7 }, await fixture.Service.GetAsync(older.Id, fixture.TenantId, UserId, "Token", contextNip: Nip));
    }

    [Fact]
    public async Task InternalEndpointPersistsAndFindsExplicitContext()
    {
        await using var fixture = new Fixture();
        var controller = new InternalController(fixture.Service, null!, null!, null!, null!);
        Assert.IsType<OkObjectResult>(await controller.SaveTokenAsync(new() { TenantId = fixture.TenantId, UserId = UserId, Token = "secret", ContextNip = Nip }, default));
        var stored = await fixture.Db.KsefCredentials.SingleAsync();
        Assert.Equal(Nip, stored.ContextNip);
        var result = Assert.IsType<OkObjectResult>(await controller.GetTokenInfoAsync(fixture.TenantId, UserId, Nip, default));
        Assert.True(JsonSerializer.SerializeToElement(result.Value).GetProperty("Exists").GetBoolean());
        result = Assert.IsType<OkObjectResult>(await controller.GetTokenInfoAsync(fixture.TenantId, UserId, OtherNip, default));
        Assert.False(JsonSerializer.SerializeToElement(result.Value).GetProperty("Exists").GetBoolean());
    }

    [Fact]
    public async Task ApiUsesAuthorizedTenantTaxEntityAndJwtUserForInternalRequests()
    {
        Guid tenant = Guid.NewGuid();
        var taxService = new TenantStub(tenant);
        var requests = new List<(Uri Uri, string? Body)>();
        using var http = new HttpClient(new CredentialServiceEnvironmentTests.StubHandler(async (request, _) =>
        {
            requests.Add((request.RequestUri!, request.Content is null ? null : await request.Content.ReadAsStringAsync()));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { CredentialId = Guid.NewGuid(), Exists = true }) };
        })) { BaseAddress = new Uri("https://credential.test/") };
        var controller = new ApiController(new CredentialServiceClient(new ClientFactory(http)), taxService)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, UserId) }, "test"))
                }
            }
        };
        controller.HttpContext.Items["TenantId"] = tenant;
        Assert.IsType<OkObjectResult>(await controller.SaveTokenAsync(new() { Token = "secret" }, default));
        Assert.IsType<OkObjectResult>((await controller.GetTokenInfoAsync(default)).Result);
        using var saved = JsonDocument.Parse(requests[0].Body!);
        Assert.Equal(Nip, saved.RootElement.GetProperty("contextNip").GetString());
        Assert.Equal(UserId, saved.RootElement.GetProperty("userId").GetString());
        Assert.Equal(tenant, saved.RootElement.GetProperty("tenantId").GetGuid());
        Assert.Contains("contextNip=" + Nip, requests[1].Uri.Query);
        Assert.Equal(2, taxService.Reads);
        // Without the tenant established by authorization no internal request can be sent.
        controller.HttpContext.Items.Remove("TenantId");
        Assert.IsType<ForbidResult>(await controller.SaveTokenAsync(new() { Token = "secret" }, default));
        Assert.Equal(2, requests.Count);
        Assert.Null(typeof(xKsef.Api.Contracts.Credentials.SaveKsefTokenRequest).GetProperty("ContextNip"));
    }

    [Theory]
    [InlineData(null, 404)]
    [InlineData("", 500)]
    [InlineData("123", 500)]
    [InlineData("１２３４５６７８９０", 500)]
    public async Task ApiRejectsMissingTaxEntityOrInvalidServerNipWithoutSendingToken(string? nip, int status)
    {
        Guid tenant = Guid.NewGuid();
        using var http = new HttpClient(new CredentialServiceEnvironmentTests.StubHandler((_, _) => throw new InvalidOperationException("No internal request is allowed.")));
        var controller = new ApiController(new CredentialServiceClient(new ClientFactory(http)), new TenantStub(tenant) { ServerNip = nip })
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, UserId) }, "test")) }
            }
        };
        controller.HttpContext.Items["TenantId"] = tenant;
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>(await controller.SaveTokenAsync(new() { Token = "secret" }, default)).StatusCode);
        Assert.Equal(status, Assert.IsAssignableFrom<ObjectResult>((await controller.GetTokenInfoAsync(default)).Result).StatusCode);
    }
    private static EncryptedSecret Envelope(KsefCredential credential) => new()
    {
        EncryptedData = credential.EncryptedData, EncryptedDek = credential.EncryptedDek, Nonce = credential.Nonce,
        AuthTag = credential.AuthTag, ProtectionType = credential.ProtectionType
    };

    private sealed class Fixture : IAsyncDisposable
    {
        public Guid TenantId { get; } = Guid.NewGuid();
        public DateTimeOffset ValidUntil { get; } = DateTimeOffset.UtcNow.AddHours(1);
        private readonly KsefSessionTestStore _store = new();
        public CredentialsDbContext Db { get; }
        public ContextualSecretEncryptionService Crypto { get; }
        public KsefCredentialService Service { get; }
        public Fixture()
        {
            Db = _store.CreateContext();
            Crypto = new(_store.Protector);
            Service = _store.CreateService(Db);
        }

        public async Task<KsefCredential> SeedSource(string? nip)
        {
            var credential = new KsefCredential { Id = Guid.NewGuid(), TenantId = TenantId, UserId = UserId, CredentialType = "Token", ContextNip = nip };
            SetSecret(credential, [7]);
            Db.Add(credential);
            await Db.SaveChangesAsync();
            return credential;
        }

        public async Task SeedSession(Guid sourceId, Guid? tenant = null)
        {
            var source = await Db.KsefCredentials.SingleAsync(c => c.Id == sourceId);
            string? originalNip = source.ContextNip;
            source.ContextNip = Nip;
            await Db.SaveChangesAsync();
            await Service.SaveSessionTokensAsync(TenantId, UserId, sourceId, Nip, "access", ValidUntil, "refresh", ValidUntil);
            source.ContextNip = originalNip;
            await Db.SaveChangesAsync();
            // A deliberately mismatched tenant is representable; a mismatched owner is prevented by the owner FK.
            if (tenant.HasValue) await Db.KsefSessions.ExecuteUpdateAsync(s => s.SetProperty(x => x.TenantId, tenant.Value));
        }

        private void SetSecret(KsefCredential credential, byte[] data)
        {
            var encrypted = Crypto.Encrypt(data, SecretEncryptionContext.KsefToken(credential.Id, credential.UserId, credential.TenantId!.Value, Nip));
            credential.EncryptedData = encrypted.EncryptedData;
            credential.EncryptedDek = encrypted.EncryptedDek;
            credential.Nonce = encrypted.Nonce;
            credential.AuthTag = encrypted.AuthTag;
        }

        public async ValueTask DisposeAsync()
        {
            await Db.DisposeAsync();
            _store.Dispose();
        }
    }

    private sealed class TestProtector : IKeyProtector
    {
        public byte[] Protect(byte[] key) => key.ToArray();
        public byte[] Unprotect(byte[] protectedKey) => protectedKey.ToArray();
    }

    private sealed class ClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class TenantStub(Guid expectedTenant) : ITenantService
    {
        public int Reads { get; private set; }
        public string? ServerNip { get; init; } = Nip;
        public Task<TaxEntity?> GetTaxEntityAsync(Guid tenantId, CancellationToken cancellationToken = default)
        {
            Assert.Equal(expectedTenant, tenantId);
            Reads++;
            return Task.FromResult<TaxEntity?>(ServerNip is null ? null : new() { Id = Guid.NewGuid(), Nip = ServerNip });
        }
        public Task<CreateTenantResult> CreateAsync(string name, string nip, string taxEntityName, string userLogin, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<IReadOnlyList<TenantDto>> GetUserTenantsAsync(string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Guid?> GetPreferredKsefCredentialIdAsync(Guid tenantId, string userId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task SetPreferredKsefCredentialIdAsync(Guid tenantId, string userId, Guid? credentialId, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
