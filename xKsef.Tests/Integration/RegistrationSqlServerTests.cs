using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using xKsef.Api.Authentication;
using xKsef.Api.Authorization;
using xKsef.Api.Controllers;
using xKsef.Application.Credentials;
using xKsef.Contracts.Authentication;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Credentials;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

[Collection(LocalDbIntegrationCollection.Name)]
public sealed class RegistrationSqlServerTests
{
    [Fact]
    public async Task NewAccountCanLogInAndHasNoRolesOrTenants()
    {
        await using var api = await RegistrationApi.CreateAsync();
        using var registered = await api.RegisterAsync(api.Prefix + "-new", api.Prefix + "@example.com");
        Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        string id = (await registered.Content.ReadFromJsonAsync<RegisterResponse>())!.UserId;
        using (var scope = api.Host.Services.CreateScope())
        {
            var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var user = (await users.FindByIdAsync(id))!;
            Assert.True(await users.CheckPasswordAsync(user, Password));
            Assert.NotEqual(Password, user.PasswordHash);
            Assert.Empty(await users.GetRolesAsync(user));
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.UserTenants.AnyAsync(membership => membership.UserId == id));
            Assert.False(await db.UserRoles.AnyAsync(role => role.UserId == id));
        }
        using var login = await api.Client.PostAsJsonAsync("/api/auth/login", new { Login = api.Prefix + "-new", Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!;
        Assert.Equal("Bearer", token.TokenType);
        Assert.False(string.IsNullOrWhiteSpace(token.AccessToken));
        using var tenantsRequest = new HttpRequestMessage(HttpMethod.Get, "/api/tenants");
        tenantsRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var tenants = await api.Client.SendAsync(tenantsRequest);
        Assert.Equal(HttpStatusCode.OK, tenants.StatusCode);
        using var body = JsonDocument.Parse(await tenants.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.ValueKind);
        Assert.Empty(body.RootElement.EnumerateArray());
        using var operatorRequest = new HttpRequestMessage(HttpMethod.Post, "/api/users")
        {
            Content = JsonContent.Create(new { Login = api.Prefix + "-other", Email = api.Prefix + "-other@example.com", Password })
        };
        operatorRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var forbidden = await api.Client.SendAsync(operatorRequest);
        Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        Assert.Equal(1, await api.CountOwnUsersAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DuplicateLoginAndEmailAreRejectedUsingIdentityNormalization(bool sameLogin)
    {
        await using var api = await RegistrationApi.CreateAsync();
        using var first = await api.RegisterAsync(api.Prefix + "-first", api.Prefix + "-first@example.com");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var duplicate = await api.RegisterAsync(sameLogin ? (api.Prefix + "-first").ToUpperInvariant() : api.Prefix + "-second",
            sameLogin ? api.Prefix + "-second@example.com" : (api.Prefix + "-first@example.com").ToUpperInvariant());
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains(sameLogin ? "DuplicateUserName" : "DuplicateEmail", await duplicate.Content.ReadAsStringAsync());
        Assert.Equal(1, await api.CountOwnUsersAsync());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task ConcurrentRegistrationsHitTheRealSqlUniqueIndexAndReturn400(bool sameLogin)
    {
        await using var api = await RegistrationApi.CreateAsync();
        api.Writes.SynchronizeTwoInserts = true;
        var responses = await Task.WhenAll(api.RegisterAsync(api.Prefix + "-first", api.Prefix + "-first@example.com"),
            api.RegisterAsync(sameLogin ? (api.Prefix + "-first").ToUpperInvariant() : api.Prefix + "-second",
                sameLogin ? api.Prefix + "-second@example.com" : (api.Prefix + "-first@example.com").ToUpperInvariant()));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
            string body = await rejected.Content.ReadAsStringAsync();
            Assert.Contains("DuplicateAccount", body);
            Assert.DoesNotContain("SqlException", body);
            Assert.DoesNotContain("EmailIndex", body);
            Assert.DoesNotContain("UserNameIndex", body);
            Assert.DoesNotContain(Password, body);
            Assert.Equal(2, api.Writes.InsertAttempts);
            Assert.Contains(Assert.Single(api.Writes.SqlErrors), new[] { 2601, 2627 });
            Assert.Equal(1, await api.CountOwnUsersAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task InvalidPasswordDoesNotCreateAnyAccount()
    {
        await using var api = await RegistrationApi.CreateAsync();
        using var invalid = await api.RegisterAsync(api.Prefix + "-invalid", api.Prefix + "@example.com", "weak");
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        Assert.Contains("PasswordTooShort", await invalid.Content.ReadAsStringAsync());
        Assert.Equal(0, await api.CountOwnUsersAsync());
    }

    private const string Password = "Sql-Registration-Test-123!";

    private sealed class RegistrationApi : IAsyncDisposable
    {
        public string Prefix { get; } = "m1011-" + Guid.NewGuid().ToString("N");
        public IHost Host { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public RegistrationWrites Writes { get; } = new();

        public static async Task<RegistrationApi> CreateAsync()
        {
            var api = new RegistrationApi();
            var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.Test.json", optional: false).UseIsolatedSqlServer()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Jwt:Issuer"] = "registration-sql-tests", ["Jwt:Audience"] = "registration-sql-tests",
                    ["Jwt:SigningKey"] = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32)), ["Jwt:ExpirationMinutes"] = "5"
                }).Build();
            string connectionString = configuration.GetConnectionString("DefaultConnection")!;
            await SqlServerTestDatabase.VerifyAsync(connectionString);
            try
            {
                api.Host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureLogging(logging => logging.ClearProviders())
                    .ConfigureServices(services =>
                    {
                        services.AddSingleton<IConfiguration>(configuration);
                        services.AddDatabaseInfrastructure(configuration);
                        services.AddDbContext<AppDbContext>(options => options.AddInterceptors(api.Writes));
                        services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
                        services.AddApplicationOperatorAuthorization();
                        services.AddRegistrationRateLimiting();
                        services.AddRateLimiter(options => options.AddFixedWindowLimiter("login", limiter => { limiter.PermitLimit = 5; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; }));
                        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true, ValidIssuer = "registration-sql-tests", ValidateAudience = true, ValidAudience = "registration-sql-tests",
                            ValidateLifetime = true, RequireExpirationTime = true, ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(configuration["Jwt:SigningKey"]!)), RequireSignedTokens = true,
                            ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero
                        });
                        services.AddHttpClient("CredentialService").ConfigurePrimaryHttpMessageHandler(() => new RejectExternalCalls());
                        services.AddScoped<ICredentialServiceClient, CredentialServiceClient>();
                    }).Configure(app =>
                    {
                        app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.UseEndpoints(endpoints => endpoints.MapControllers());
                    })).StartAsync();
                api.Client = api.Host.GetTestClient();
                using var scope = api.Host.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Assert.True(db.Database.IsSqlServer());
                Assert.Equal(db.Database.GetMigrations(), await db.Database.GetAppliedMigrationsAsync());
                Assert.False(await db.Users.AnyAsync(user => user.UserName != null && user.UserName.StartsWith(api.Prefix)));
                return api;
            }
            catch { await api.DisposeAsync(); throw; }
        }

        public Task<HttpResponseMessage> RegisterAsync(string login, string email, string password = Password) => Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Login = login, Email = email, Password = password });
        public async Task<int> CountOwnUsersAsync()
        {
            using var scope = Host.Services.CreateScope();
            return await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync(user => user.UserName != null && user.UserName.StartsWith(Prefix));
        }
        public async ValueTask DisposeAsync()
        {
            try
            {
                if (Host is not null)
                {
                    using var scope = Host.Services.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    // A fresh GUID prefix isolates this fixture, including failed concurrent registrations.
                    var ids = await db.Users.Where(user => user.UserName != null && user.UserName.StartsWith(Prefix)).Select(user => user.Id).ToArrayAsync();
                    await db.Users.Where(user => ids.Contains(user.Id)).ExecuteDeleteAsync();
                    Assert.False(await db.Users.AnyAsync(user => user.UserName != null && user.UserName.StartsWith(Prefix)));
                }
            }
            finally { Client?.Dispose(); Host?.Dispose(); }
        }
    }

    private sealed class RegistrationWrites : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _attempts;
        public int InsertAttempts => _attempts;
        public bool SynchronizeTwoInserts { get; set; }
        public ConcurrentBag<int> SqlErrors { get; } = new();
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (data.Context!.ChangeTracker.Entries<ApplicationUser>().Any(entry => entry.State == EntityState.Added))
            {
                int attempt = Interlocked.Increment(ref _attempts);
                if (SynchronizeTwoInserts)
                {
                    if (attempt == 2) _bothArrived.TrySetResult();
                    await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
                }
            }
            return result;
        }
        public override Task SaveChangesFailedAsync(DbContextErrorEventData data, CancellationToken cancellationToken = default)
        {
            for (Exception? error = data.Exception; error is not null; error = error.InnerException)
                if (error is SqlException sql) SqlErrors.Add(sql.Number);
            return Task.CompletedTask;
        }
    }

    private sealed class RejectExternalCalls : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            => throw new InvalidOperationException("External service calls are forbidden in these registration tests.");
    }
}
