using System.Data.Common;
using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using xKsef.Api.Authentication;
using xKsef.Api.Authorization;
using xKsef.Api.Controllers;
using xKsef.Api.ExceptionHandlers;
using xKsef.Application.Authentication;
using xKsef.Application.Credentials;
using xKsef.Application.Gus;
using xKsef.Application.Invoices.Pdf;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Credentials;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    private sealed class Fixture : IAsyncDisposable
    {
        public string Prefix { get; } = "m132-" + Guid.NewGuid().ToString("N");
        public string UserId { get; private set; } = null!; public string OtherId { get; private set; } = null!;
        public string Login => Prefix + "-one"; public string OtherLogin => Prefix + "-two";
        public string KeyPath { get; } = Path.Combine(Path.GetTempPath(), "xksef-browser-tests-" + Guid.NewGuid().ToString("N"));
        public BrowserTestClock Clock { get; } = new(); public BrowserRequestGate Gate { get; } = new(); public StoreFailure Failure { get; } = new();
        public InvoiceReadSqlInterceptor InvoiceSql { get; } = new();
        public DocumentPdfGenerator Pdf { get; } = new();
        public SecurityGusClient Gus { get; } = new();
        public IHost Host { get; private set; } = null!; public HttpClient Client { get; private set; } = null!;
        private IConfiguration Configuration { get; set; } = null!; private readonly List<Guid> _tenants = []; private readonly List<Guid> _taxEntities = [];
        public IServiceScope Scope() => Host.Services.CreateScope();
        public Browser Browser() => new(this);
        public static async Task<Fixture> CreateAsync()
        {
            var f = new Fixture();
            f.Configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.Test.json").UseIsolatedSqlServer()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = "browser-tests", ["Jwt:Audience"] = "browser-tests", ["Jwt:ExpirationMinutes"] = "5",
                    ["Jwt:SigningKey"] = Convert.ToBase64String(RandomNumberGenerator.GetBytes(64)),
                    ["DataProtection:KeyRingPath"] = f.KeyPath, ["DataProtection:ApplicationName"] = f.Prefix,
                    ["BrowserSessions:Cleanup:Enabled"] = "false"
                }).Build();
            await SqlServerTestDatabase.VerifyAsync(f.Configuration.GetConnectionString("DefaultConnection")!);
            try
            {
                await f.StartAsync(); using var scope = f.Scope(); var service = scope.ServiceProvider.GetRequiredService<IUserCreationService>();
                var one = await service.CreateAsync(f.Login, f.Login + "@example.com", Password); var two = await service.CreateAsync(f.OtherLogin, f.OtherLogin + "@example.com", Password);
                Assert.Empty(one.Errors); Assert.Empty(two.Errors); f.UserId = one.UserId!; f.OtherId = two.UserId!;
                return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        private async Task StartAsync()
        {
            Host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureServices((context, services) =>
                {
                    services.AddSingleton(Configuration); services.AddDatabaseInfrastructure(Configuration);
                    services.AddDbContext<AppDbContext>(options => options.AddInterceptors(Failure, InvoiceSql));
                    // Query uses the real SQL query service; all legacy/KSeF invoice operations fail immediately.
                    services.AddScoped<IInvoiceService, UnexpectedInvoiceService>();
                    services.AddSingleton<IInvoicePdfGenerator>(Pdf);
                    new IdentityBuilder(typeof(ApplicationUser), services).AddDefaultTokenProviders();
                    services.AddSingleton<TimeProvider>(Clock); services.AddSingleton(Gate);
                    services.AddHttpContextAccessor();
                    services.AddSingleton<IGusBirClient>(provider =>
                    {
                        Gus.ContextAccessor = provider.GetRequiredService<Microsoft.AspNetCore.Http.IHttpContextAccessor>();
                        return Gus;
                    });
                    services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly).AddApplicationPart(typeof(BrowserTestController).Assembly);
                    services.AddScoped<TenantAuthorizationFilter>(); services.AddScoped<CredentialManagementAuthorizationFilter>(); services.AddApplicationOperatorAuthorization();
                    services.AddApiAuthentication(Configuration, context.HostingEnvironment);
                    services.AddRegistrationRateLimiting();
                    services.AddRateLimiter(options => { options.RejectionStatusCode = 429; options.AddFixedWindowLimiter("login", limiter => { limiter.PermitLimit = 5; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; }); });
                    services.AddExceptionHandler<BrowserSessionExceptionHandler>(); services.AddExceptionHandler<GusExceptionHandler>(); services.AddExceptionHandler<SalesInvoiceExceptionHandler>(); services.AddProblemDetails();
                    services.AddHttpClient("CredentialService").ConfigurePrimaryHttpMessageHandler(() => new RejectExternalCalls());
                    services.AddScoped<ICredentialServiceClient, CredentialServiceClient>();
                }).Configure(app =>
                {
                    app.UseExceptionHandler(); app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.UseEndpoints(endpoints => endpoints.MapControllers());
                })).StartAsync();
            Client = Host.GetTestClient(); Client.BaseAddress = new Uri("https://localhost");
        }
        public async Task EnableCleanupAsync()
        {
            Configuration["BrowserSessions:Cleanup:Enabled"] = "true"; Configuration["BrowserSessions:Cleanup:Interval"] = "00:00:00.02";
            Configuration["BrowserSessions:Cleanup:BatchSize"] = "1"; Configuration["BrowserSessions:Cleanup:MaxBatches"] = "1";
            await RestartAsync();
        }
        public async Task RestartAsync() { Client.Dispose(); await Host.StopAsync(); Host.Dispose(); await StartAsync(); }
        public async Task<Guid> CreateTenantAsync(string? userId = null)
        {
            Assert.Contains(userId ?? UserId, new[] { UserId, OtherId });
            using var scope = Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var tax = new TaxEntity { Id = Guid.NewGuid(), Nip = "8" + RandomNumberGenerator.GetInt32(100000000, 999999999).ToString(System.Globalization.CultureInfo.InvariantCulture), Name = Prefix };
            var tenant = new Tenant { Name = Prefix, TaxEntity = tax }; _taxEntities.Add(tax.Id); _tenants.Add(tenant.Id);
            db.Tenants.Add(tenant); db.UserTenants.Add(new UserTenant { TenantId = tenant.Id, UserId = userId ?? UserId, Role = "User" }); await db.SaveChangesAsync(); return tenant.Id;
        }
        public async ValueTask DisposeAsync()
        {
            Failure.Enabled = false; Gate.Release.TrySetResult();
            try
            {
                if (Host is not null)
                {
                    using var scope = Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    await db.Invoices.Where(row => _tenants.Contains(row.TenantId)).ExecuteDeleteAsync();
                    await db.SalesInvoices.Where(row => _tenants.Contains(row.TenantId)).ExecuteDeleteAsync();
                    var contractors = db.Contractors.Where(row => _tenants.Contains(row.TenantId));
                    var contractorData = db.ContractorsData.Where(row => contractors.Any(contractor => contractor.Id == row.ContractorId));
                    var addresses = db.Addresses.Where(row => contractorData.Any(data => data.Id == row.ContractorsDataId));
                    await db.AddressesData.Where(row => addresses.Any(address => address.Id == row.AddressId)).ExecuteDeleteAsync();
                    await addresses.ExecuteDeleteAsync();
                    await contractorData.ExecuteDeleteAsync();
                    await contractors.ExecuteDeleteAsync();
                    await db.UserTenants.Where(row => _tenants.Contains(row.TenantId)).ExecuteDeleteAsync();
                    await db.Tenants.Where(row => _tenants.Contains(row.Id)).ExecuteDeleteAsync();
                    await db.TaxEntities.Where(row => _taxEntities.Contains(row.Id) && !db.Tenants.Any(tenant => tenant.TaxEntityId == row.Id)).ExecuteDeleteAsync();
                    await db.Users.Where(user => user.UserName != null && user.UserName.StartsWith(Prefix)).ExecuteDeleteAsync();
                    Assert.False(await db.Users.AnyAsync(user => user.UserName != null && user.UserName.StartsWith(Prefix)));
                    Assert.False(await db.Invoices.AnyAsync(row => _tenants.Contains(row.TenantId)));
                    Assert.False(await db.Tenants.AnyAsync(row => _tenants.Contains(row.Id)));
                    Assert.False(await db.TaxEntities.AnyAsync(row => _taxEntities.Contains(row.Id)));
                }
            }
            finally
            {
                Client?.Dispose(); if (Host is not null) { await Host.StopAsync(); Host.Dispose(); }
                string full = Path.GetFullPath(KeyPath); string temp = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Path.GetTempPath())) + Path.DirectorySeparatorChar;
                if (!full.StartsWith(temp, StringComparison.OrdinalIgnoreCase) || !Path.GetFileName(full).StartsWith("xksef-browser-tests-", StringComparison.Ordinal)) throw new InvalidOperationException("Unexpected test key directory.");
                if (Directory.Exists(full)) Directory.Delete(full, true);
            }
        }
    }
    private sealed class Browser(Fixture fixture)
    {
        private readonly Dictionary<string, string> _cookies = new(); public string? Token { get; set; }
        public string CookieHeader => string.Join("; ", _cookies.Select(pair => pair.Key + "=" + pair.Value));
        public Browser Clone() { var other = new Browser(fixture) { Token = Token }; foreach (var cookie in _cookies) other._cookies.Add(cookie.Key, cookie.Value); return other; }
        public async Task CsrfAsync()
        {
            using var response = await SendAsync(HttpMethod.Get, "/api/browser-auth/csrf"); Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Token = json.RootElement.GetProperty("requestToken").GetString();
        }
        public async Task<HttpResponseMessage> LoginAsync(string? login = null)
        {
            if (Token is null) await CsrfAsync(); return await SendAsync(HttpMethod.Post, "/api/browser-auth/login", new { Login = login ?? fixture.Login, Password });
        }
        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, object? body = null, string? authorization = null, Guid? tenantId = null)
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null) request.Content = JsonContent.Create(body);
            if (_cookies.Count > 0) request.Headers.TryAddWithoutValidation("Cookie", string.Join("; ", _cookies.Select(pair => pair.Key + "=" + pair.Value)));
            if (Token is not null) request.Headers.TryAddWithoutValidation(BrowserAuthentication.CsrfHeader, Token);
            if (authorization is not null) request.Headers.TryAddWithoutValidation("Authorization", authorization);
            if (tenantId is not null) request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId.Value.ToString("D"));
            var response = await fixture.Client.SendAsync(request);
            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
                foreach (string cookie in cookies) { string value = cookie.Split(';')[0]; int index = value.IndexOf('='); _cookies[value[..index]] = value[(index + 1)..]; }
            return response;
        }
    }
    private sealed class StoreFailure : DbCommandInterceptor
    {
        public bool Enabled { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { if (Enabled) throw new IOException("simulated store outage"); return ValueTask.FromResult(result); }
    }
    private sealed class RejectExternalCalls : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => throw new InvalidOperationException("External service calls are forbidden.");
    }
}

public sealed class BrowserTestClock : TimeProvider
{
    private long _ticks = DateTimeOffset.UtcNow.UtcTicks;
    public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
    public void Advance(TimeSpan duration) => Interlocked.Add(ref _ticks, duration.Ticks);
}
public sealed class BrowserRequestGate
{
    public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public int Writes;
}
[ApiController]
[Authorize]
[Route("test/browser")]
public sealed class BrowserTestController(BrowserRequestGate gate) : ControllerBase
{
    [HttpGet("identity")]
    public IActionResult IdentityInfo() => Ok(new { UserId = User.FindFirstValue(ClaimTypes.NameIdentifier), Identities = User.Identities.Count() });
    [HttpGet("jwt-identity")]
    [Authorize(AuthenticationSchemes = JwtBearerDefaults.AuthenticationScheme)]
    public IActionResult JwtIdentityInfo() => IdentityInfo();
    [HttpPost("write")]
    public IActionResult Write() { Interlocked.Increment(ref gate.Writes); return IdentityInfo(); }
    [HttpGet("tenant")]
    [ServiceFilter(typeof(TenantAuthorizationFilter))]
    public IActionResult Tenant() => Ok();
    [HttpGet("slow")]
    public async Task<IActionResult> Slow(CancellationToken cancellationToken)
    {
        gate.Arrived.TrySetResult(); await gate.Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken); return Ok();
    }
}
