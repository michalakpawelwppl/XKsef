using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using xKsef.Api.Authorization;
using xKsef.Api.Controllers;
using xKsef.Application.Credentials;
using xKsef.Contracts.Tenants;
using xKsef.Domain.Invoices;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Credentials;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

[Collection(LocalDbIntegrationCollection.Name)]
public sealed class TenantUsersTests
{
    [Fact]
    public async Task OnlyCurrentTenantAdminCanListOrManageUsers()
    {
        await using var api = await Fixture.CreateAsync();
        foreach (string? actor in new string?[] { null, api.B, api.C, api.D })
        {
            foreach (var method in new[] { HttpMethod.Get, HttpMethod.Post, HttpMethod.Put, HttpMethod.Delete })
            {
                string suffix = method == HttpMethod.Put ? "/" + api.B + "/role" : method == HttpMethod.Delete ? "/" + api.B : "";
                using var denied = await api.SendAsync(method, actor, api.TenantA, suffix, method == HttpMethod.Post ? new { Identifier = api.LoginD, Role = "User" } : method == HttpMethod.Put ? new { Role = "Admin" } : null, globalAdmin: true);
                Assert.Equal(actor is null ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden, denied.StatusCode);
            }
        }
        using var allowed = await api.SendAsync(HttpMethod.Get, api.A, api.TenantA);
        Assert.Equal(HttpStatusCode.OK, allowed.StatusCode);
        var members = (await allowed.Content.ReadFromJsonAsync<List<TenantUserDto>>())!;
        Assert.Equal(new[] { api.A, api.B }.Order(), members.Select(member => member.UserId).Order());
        Assert.DoesNotContain(members, member => member.UserId == api.C || member.UserId == api.D);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactLoginOrEmailAddsOnlyAnExistingAccount(bool email)
    {
        await using var api = await Fixture.CreateAsync();
        using var added = await api.SendAsync(HttpMethod.Post, api.A, api.TenantA, body: new { Identifier = (email ? api.EmailD : api.LoginD).ToUpperInvariant(), Role = "User" });
        Assert.Equal(HttpStatusCode.Created, added.StatusCode);
        using var duplicate = await api.SendAsync(HttpMethod.Post, api.A, api.TenantA, body: new { Identifier = api.LoginD, Role = "Admin" });
        Assert.Equal(HttpStatusCode.Conflict, duplicate.StatusCode);
        using var scope = api.Host.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = await db.UserTenants.SingleAsync(member => member.UserId == api.D && member.TenantId == api.TenantA);
        Assert.Equal("User", row.Role);
        Assert.Null(row.PreferredKsefCredentialId);
        Assert.False(await db.UserTenants.AnyAsync(member => member.TenantId == api.TenantB && member.UserId == api.D));
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().GetRolesAsync((await db.Users.FindAsync(api.D))!));
    }

    [Fact]
    public async Task RoleChangePreservesPreferencesAndRevocationBlocksTheSameJwt()
    {
        await using var api = await Fixture.CreateAsync();
        using var promoted = await api.SendAsync(HttpMethod.Put, api.A, api.TenantA, "/" + api.B + "/role", new { Role = "Admin" });
        Assert.Equal(HttpStatusCode.OK, promoted.StatusCode);
        using (var scope = api.Host.Services.CreateScope())
            Assert.Equal(api.Preference, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserTenants.SingleAsync(member => member.UserId == api.B && member.TenantId == api.TenantA)).PreferredKsefCredentialId);
        using var before = await api.ProbeAsync(api.B, api.TenantA);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);
        Assert.Contains("first", await before.Content.ReadAsStringAsync());
        using var removed = await api.SendAsync(HttpMethod.Delete, api.A, api.TenantA, "/" + api.B);
        Assert.Equal(HttpStatusCode.OK, removed.StatusCode);
        using var after = await api.ProbeAsync(api.B, api.TenantA);
        Assert.Equal(HttpStatusCode.Forbidden, after.StatusCode);
        using var manage = await api.SendAsync(HttpMethod.Get, api.B, api.TenantA);
        Assert.Equal(HttpStatusCode.Forbidden, manage.StatusCode);
        using var own = await api.ProbeAsync(api.C, api.TenantB);
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Contains("second", await own.Content.ReadAsStringAsync());
        using var foreign = await api.ProbeAsync(api.A, api.TenantB);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        using var readded = await api.SendAsync(HttpMethod.Post, api.A, api.TenantA, body: new { Identifier = api.LoginB, Role = "User" });
        Assert.Equal(HttpStatusCode.Created, readded.StatusCode);
        using var verification = api.Host.Services.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.UserTenants.SingleAsync(member => member.UserId == api.B && member.TenantId == api.TenantA)).PreferredKsefCredentialId);
        Assert.Equal(2, await db.Invoices.CountAsync(invoice => invoice.KsefNumber == api.Prefix));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastAdminCannotRemoveOrDemoteThemself(bool remove)
    {
        await using var api = await Fixture.CreateAsync();
        using var response = await api.SendAsync(remove ? HttpMethod.Delete : HttpMethod.Put, api.A, api.TenantA,
            "/" + api.A + (remove ? "" : "/role"), remove ? null : new { Role = "User" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("LastAdmin", await response.Content.ReadAsStringAsync());
        Assert.Equal(1, await api.AdminCountAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentSelfChangesCannotRemoveBothAdmins(bool remove)
    {
        await using var api = await Fixture.CreateAsync(twoAdmins: true);
        api.LockGate.Enabled = true;
        var responses = await Task.WhenAll(api.SendAsync(remove ? HttpMethod.Delete : HttpMethod.Put, api.A, api.TenantA, "/" + api.A + (remove ? "" : "/role"), remove ? null : new { Role = "User" }),
            api.SendAsync(remove ? HttpMethod.Delete : HttpMethod.Put, api.B, api.TenantA, "/" + api.B + (remove ? "" : "/role"), remove ? null : new { Role = "User" }));
        try { Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK); Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict); Assert.Equal(1, await api.AdminCountAsync()); }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task AdminRemovedWhileWaitingForTheLockCannotMutate()
    {
        await using var api = await Fixture.CreateAsync(twoAdmins: true);
        api.LockGate.Enabled = true;
        var responses = await Task.WhenAll(api.SendAsync(HttpMethod.Delete, api.A, api.TenantA, "/" + api.B), api.SendAsync(HttpMethod.Delete, api.B, api.TenantA, "/" + api.A));
        try { Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK); Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Forbidden); Assert.Equal(1, await api.AdminCountAsync()); }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task ConcurrentGrantsCreateExactlyOneMembership()
    {
        await using var api = await Fixture.CreateAsync();
        api.LockGate.Enabled = true;
        var body = new { Identifier = api.LoginD, Role = "User" };
        var responses = await Task.WhenAll(api.SendAsync(HttpMethod.Post, api.A, api.TenantA, body: body), api.SendAsync(HttpMethod.Post, api.A, api.TenantA, body: body));
        try { Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created); Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var response in responses) response.Dispose(); }
        using var scope = api.Host.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserTenants.CountAsync(member => member.TenantId == api.TenantA && member.UserId == api.D));
    }

    [Theory]
    [InlineData("unknown", "User", HttpStatusCode.NotFound)]
    [InlineData("", "User", HttpStatusCode.BadRequest)]
    [InlineData("known", "GlobalAdmin", HttpStatusCode.BadRequest)]
    [InlineData("known", "Admin\n", HttpStatusCode.BadRequest)]
    public async Task InvalidGrantsHaveControlledErrors(string identifier, string role, HttpStatusCode status)
    {
        await using var api = await Fixture.CreateAsync();
        using var response = await api.SendAsync(HttpMethod.Post, api.A, api.TenantA, body: new { Identifier = identifier == "known" ? api.LoginD : identifier, Role = role });
        Assert.Equal(status, response.StatusCode);
        Assert.DoesNotContain("SqlException", await response.Content.ReadAsStringAsync());
        using var scope = api.Host.Services.CreateScope();
        Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserTenants.AnyAsync(member => member.TenantId == api.TenantA && member.UserId == api.D));
    }

    [Fact]
    public async Task MissingMembershipAndInactiveTenantDoNotLeakAccess()
    {
        await using var api = await Fixture.CreateAsync();
        using var missing = await api.SendAsync(HttpMethod.Delete, api.A, api.TenantA, "/" + api.D);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var scope = api.Host.Services.CreateScope()) { var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); (await db.Tenants.FindAsync(api.TenantA))!.IsActive = false; await db.SaveChangesAsync(); }
        using var inactive = await api.SendAsync(HttpMethod.Get, api.A, api.TenantA);
        Assert.Equal(HttpStatusCode.Forbidden, inactive.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SelfChangeReturnsTheNewCurrentRole(bool remove)
    {
        await using var api = await Fixture.CreateAsync(twoAdmins: true);
        using var changed = await api.SendAsync(remove ? HttpMethod.Delete : HttpMethod.Put, api.A, api.TenantA, "/" + api.A + (remove ? "" : "/role"), remove ? null : new { Role = "User" });
        Assert.Equal(HttpStatusCode.OK, changed.StatusCode);
        Assert.Equal(remove ? null : "User", (await changed.Content.ReadFromJsonAsync<TenantAccessChangeDto>())!.CurrentUserRole);
        using var denied = await api.SendAsync(HttpMethod.Get, api.A, api.TenantA);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task AmbiguousLoginAndEmailDoesNotGrantAccessToEitherAccount()
    {
        await using var api = await Fixture.CreateAsync();
        using (var scope = api.Host.Services.CreateScope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await manager.FindByIdAsync(api.C))!;
            Assert.True((await manager.SetUserNameAsync(user, api.EmailD)).Succeeded);
        }
        using var response = await api.SendAsync(HttpMethod.Post, api.A, api.TenantA, body: new { Identifier = api.EmailD, Role = "User" });
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Contains("AmbiguousUser", await response.Content.ReadAsStringAsync());
        using var verification = api.Host.Services.CreateScope();
        var db = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.UserTenants.AnyAsync(member => member.TenantId == api.TenantA && (member.UserId == api.C || member.UserId == api.D)));
    }
    [Fact]
    public async Task PersonalCertificatesNeedOnlyJwtWhileTokensNeedActiveMembership()
    {
        await using var api = await Fixture.CreateAsync();
        foreach (var actor in new[] { api.A, api.B, api.C, api.D })
        {
            using var list = await api.PersonalAsync(HttpMethod.Get, "api/ksef/credentials/certificates", actor);
            Assert.Equal(HttpStatusCode.OK, list.StatusCode);
            Assert.Equal(actor, ((PersonalCredentialProbe)api.PersonalCredentials).LastOwner);
            using var saved = await api.PersonalAsync(HttpMethod.Post, "api/ksef/authentication/certificate", actor, body: new { CertificatePem = "pem", EncryptedPrivateKeyPem = "encrypted", Password = "test-only" });
            Assert.Equal(HttpStatusCode.OK, saved.StatusCode);
            Assert.Equal(actor, ((PersonalCredentialProbe)api.PersonalCredentials).LastOwner);
        }
        using var anonymous = await api.PersonalAsync(HttpMethod.Get, "api/ksef/credentials/certificates", null);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var userToken = await api.PersonalAsync(HttpMethod.Post, "api/ksef/credentials/token", api.B, api.TenantA, new { Token = "local-test" });
        Assert.Equal(HttpStatusCode.OK, userToken.StatusCode);
        Assert.Equal(api.B, ((PersonalCredentialProbe)api.PersonalCredentials).LastOwner);
        using var noTenant = await api.PersonalAsync(HttpMethod.Get, "api/ksef/credentials/token", api.B);
        Assert.Equal(HttpStatusCode.BadRequest, noTenant.StatusCode);
        using var foreign = await api.PersonalAsync(HttpMethod.Get, "api/ksef/credentials/token", api.C, api.TenantA);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public string Prefix { get; } = "members-" + Guid.NewGuid().ToString("N");
        public string A { get; } = Guid.NewGuid().ToString(); public string B { get; } = Guid.NewGuid().ToString(); public string C { get; } = Guid.NewGuid().ToString(); public string D { get; } = Guid.NewGuid().ToString();
        public Guid TenantA { get; } = Guid.NewGuid(); public Guid TenantB { get; } = Guid.NewGuid(); public Guid Preference { get; } = Guid.NewGuid(); private Guid TaxId { get; } = Guid.NewGuid();
        public string LoginB => Prefix + "-b"; public string LoginD => Prefix + "-d"; public string EmailD => LoginD + "@example.com";
        public IHost Host { get; private set; } = null!; private HttpClient Client { get; set; } = null!;
        public LockGate LockGate { get; } = new();
        public ICredentialServiceClient PersonalCredentials { get; } = System.Reflection.DispatchProxy.Create<ICredentialServiceClient, PersonalCredentialProbe>();
        private readonly byte[] _key = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        public static async Task<Fixture> CreateAsync(bool twoAdmins = false)
        {
            var api = new Fixture();
            var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.Test.json").UseIsolatedSqlServer()
                .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Issuer"] = "members-tests", ["Jwt:Audience"] = "members-tests", ["Jwt:SigningKey"] = Convert.ToBase64String(api._key), ["Jwt:ExpirationMinutes"] = "5" }).Build();
            await SqlServerTestDatabase.VerifyAsync(configuration.GetConnectionString("DefaultConnection")!);
            api.Host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureLogging(log => log.ClearProviders()).ConfigureServices(services =>
            {
                services.AddSingleton<IConfiguration>(configuration); services.AddDatabaseInfrastructure(configuration);
                services.AddDbContext<AppDbContext>(options => options.AddInterceptors(api.LockGate));
                services.AddScoped<CredentialManagementAuthorizationFilter>(); services.AddScoped<TenantAuthorizationFilter>(); services.AddControllers().AddApplicationPart(typeof(TenantUsersController).Assembly).AddApplicationPart(typeof(TenantMembersProbeController).Assembly).AddControllersAsServices();
                services.AddTransient<KsefAuthenticationController>(_ => new(null!, null!, null!, null!, api.PersonalCredentials));
                services.AddApplicationOperatorAuthorization();
                services.AddHttpClient("CredentialService").ConfigurePrimaryHttpMessageHandler(() => new RejectExternalCalls()); services.AddSingleton(api.PersonalCredentials);
                services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters { ValidIssuer = "members-tests", ValidAudience = "members-tests", IssuerSigningKey = new SymmetricSecurityKey(api._key), ValidateIssuerSigningKey = true, ValidateLifetime = true });
            }).Configure(app => { app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseEndpoints(endpoints => endpoints.MapControllers()); })).StartAsync();
            api.Client = api.Host.GetTestClient();
            try
            {
                using var scope = api.Host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                foreach (var (id, name) in new[] { (api.A, api.Prefix + "-a"), (api.B, api.LoginB), (api.C, api.Prefix + "-c"), (api.D, api.LoginD) }) Assert.True((await users.CreateAsync(new ApplicationUser { Id = id, UserName = name, Email = name + "@example.com" })).Succeeded);
                db.TaxEntities.Add(new TaxEntity { Id = api.TaxId, Nip = Random.Shared.NextInt64(1_000_000_000L, 10_000_000_000L).ToString(System.Globalization.CultureInfo.InvariantCulture), Name = api.Prefix });
                db.Tenants.AddRange(new Tenant { Id = api.TenantA, Name = api.Prefix, TaxEntityId = api.TaxId }, new Tenant { Id = api.TenantB, Name = api.Prefix, TaxEntityId = api.TaxId });
                db.UserTenants.AddRange(new UserTenant { TenantId = api.TenantA, UserId = api.A, Role = "Admin" }, new UserTenant { TenantId = api.TenantA, UserId = api.B, Role = twoAdmins ? "Admin" : "User", PreferredKsefCredentialId = api.Preference }, new UserTenant { TenantId = api.TenantB, UserId = api.C, Role = "Admin" });
                db.Invoices.AddRange(new StoredInvoice { TenantId = api.TenantA, KsefNumber = api.Prefix, InvoiceNumber = "first", XmlBytes = [] }, new StoredInvoice { TenantId = api.TenantB, KsefNumber = api.Prefix, InvoiceNumber = "second", XmlBytes = [] });
                await db.SaveChangesAsync(); return api;
            }
            catch { await api.DisposeAsync(); throw; }
        }
        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string? actor, Guid tenant, string suffix = "", object? body = null, bool globalAdmin = false)
        {
            using var request = new HttpRequestMessage(method, $"/api/tenants/{tenant:D}/users{suffix}");
            if (actor is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(actor, globalAdmin));
            if (body is not null) request.Content = JsonContent.Create(body);
            request.Headers.Add("X-Tenant-Id", TenantB.ToString());
            return await Client.SendAsync(request);
        }
        public async Task<HttpResponseMessage> PersonalAsync(HttpMethod method, string path, string? actor, Guid? tenant = null, object? body = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (actor is not null) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(actor));
            if (tenant.HasValue) request.Headers.Add("X-Tenant-Id", tenant.ToString());
            if (body is not null) request.Content = JsonContent.Create(body);
            return await Client.SendAsync(request);
        }        public async Task<HttpResponseMessage> ProbeAsync(string actor, Guid tenant)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, "/test/tenant-members-access");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", Token(actor)); request.Headers.Add("X-Tenant-Id", tenant.ToString()); return await Client.SendAsync(request);
        }
        private string Token(string actor, bool globalAdmin = false)
        {
            var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, actor) }; if (globalAdmin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            return new JwtSecurityTokenHandler().WriteToken(new JwtSecurityToken("members-tests", "members-tests", claims, expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(_key), SecurityAlgorithms.HmacSha256)));
        }
        public async Task<int> AdminCountAsync() { using var scope = Host.Services.CreateScope(); return await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserTenants.CountAsync(member => member.TenantId == TenantA && member.Role == "Admin"); }
        public async ValueTask DisposeAsync()
        {
            try
            {
                using var scope = Host.Services.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Invoices.Where(invoice => invoice.TenantId == TenantA || invoice.TenantId == TenantB).ExecuteDeleteAsync();
                await db.Tenants.Where(tenant => tenant.Id == TenantA || tenant.Id == TenantB).ExecuteDeleteAsync(); await db.TaxEntities.Where(tax => tax.Id == TaxId).ExecuteDeleteAsync();
                await db.Users.Where(user => user.Id == A || user.Id == B || user.Id == C || user.Id == D).ExecuteDeleteAsync(); await transaction.CommitAsync();
            }
            finally { Client?.Dispose(); Host?.Dispose(); }
        }
    }
    private sealed class LockGate : DbCommandInterceptor
    {
        public bool Enabled { get; set; } private int _arrivals; private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (Enabled && command.CommandText.Contains("UPDLOCK", StringComparison.Ordinal)) { if (Interlocked.Increment(ref _arrivals) == 2) _ready.TrySetResult(); await _ready.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken); }
            return result;
        }
    }
    private sealed class RejectExternalCalls : HttpMessageHandler { protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new InvalidOperationException("No external services in membership tests."); }
}

[ApiController, Authorize, ServiceFilter(typeof(TenantAuthorizationFilter)), Route("test/tenant-members-access")]
public sealed class TenantMembersProbeController(AppDbContext db) : ControllerBase
{
    [HttpGet] public async Task<IActionResult> GetAsync() => Ok(await db.Invoices.Where(invoice => invoice.TenantId == (Guid)HttpContext.Items["TenantId"]!).Select(invoice => invoice.InvoiceNumber).ToArrayAsync());
}
// A local backend double: never contacts CredentialService or KSeF, and verifies the JWT owner passed by API.
public class PersonalCredentialProbe : System.Reflection.DispatchProxy
{
    public string? LastOwner { get; private set; }
    protected override object? Invoke(System.Reflection.MethodInfo? method, object?[]? args)
    {
        if (method!.Name == "GetCertificatesAsync") { LastOwner = (string)args![0]!; return Task.FromResult<IReadOnlyList<xKsef.Application.Credentials.KsefCertificateInfo>>([]); }
        if (method.Name == "SaveCertificateAsync") { LastOwner = (string)args![0]!; return Task.FromResult(Guid.NewGuid()); }
        if (method.Name == "SaveTokenAsync") { LastOwner = (string)args![1]!; Assert.Equal(10, ((string)args[3]!).Length); return Task.FromResult(Guid.NewGuid()); }
        throw new InvalidOperationException("Unexpected credential operation in local authorization test.");
    }
}