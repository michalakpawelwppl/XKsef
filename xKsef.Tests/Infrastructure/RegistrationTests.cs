using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Reflection;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using xKsef.Api.Authentication;
using xKsef.Api.Authorization;
using xKsef.Api.Controllers;
using xKsef.Application.Authentication;
using xKsef.Application.Tenants;
using xKsef.Contracts.Authentication;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Migrations;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Infrastructure;

public sealed class RegistrationTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnonymousRegistrationCreatesAnUnprivilegedAccountAndCanLogIn(bool irrelevantHeaders)
    {
        await using var api = await RegistrationApi.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/auth/register")
        {
            Content = JsonContent.Create(new { Login = "newuser", Email = "new@example.com", Password = Password, UserId = "injected", Role = "Admin", TenantId = Guid.NewGuid(), ApplicationOperator = true })
        };
        if (irrelevantHeaders)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-jwt");
            request.Headers.Add("X-Tenant-Id", Guid.NewGuid().ToString());
        }
        using var response = await api.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(body.RootElement.EnumerateObject());
        string id = body.RootElement.GetProperty("userId").GetString()!;
        Assert.NotEqual("injected", id);
        using var scope = api.Host.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        var user = (await users.FindByIdAsync(id))!;
        Assert.True(await users.CheckPasswordAsync(user, Password));
        Assert.NotEqual(Password, user.PasswordHash);
        Assert.False(user.EmailConfirmed);
        Assert.Empty(await users.GetRolesAsync(user));
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Empty(await db.Roles.ToListAsync());
        Assert.Empty(await db.UserTenants.ToListAsync());
        Assert.Empty(await db.Tenants.ToListAsync());
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<ITenantService>().GetUserTenantsAsync(id));
        Assert.False(await scope.ServiceProvider.GetRequiredService<ITenantAccessService>().HasAccessAsync(id, Guid.NewGuid()));
        using var login = await api.Client.PostAsJsonAsync("/api/auth/login", new { Login = "newuser", Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = await login.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.False(string.IsNullOrWhiteSpace(token!.AccessToken));
        using var forbidden = new HttpRequestMessage(HttpMethod.Post, "/api/users") { Content = JsonContent.Create(new { Login = "other", Email = "other@example.com", Password }) };
        forbidden.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token.AccessToken);
        using var denied = await api.Client.SendAsync(forbidden);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Theory]
    [InlineData("login", "InvalidUserName")]
    [InlineData("email", "InvalidEmail")]
    [InlineData("short", "PasswordTooShort")]
    [InlineData("digit", "PasswordRequiresDigit")]
    [InlineData("lower", "PasswordRequiresLower")]
    [InlineData("upper", "PasswordRequiresUpper")]
    [InlineData("symbol", "PasswordRequiresNonAlphanumeric")]
    public async Task IdentityValidationReturnsSafeCodesWithoutCreatingAccounts(string field, string expected)
    {
        await using var api = await RegistrationApi.CreateAsync();
        string password = field switch
        {
            "short" => "Aa1!", "digit" => "Long-Password!", "lower" => "LONG-PASSWORD-123!",
            "upper" => "long-password-123!", "symbol" => "LongPassword123", _ => Password
        };
        using var response = await api.RegisterAsync(field == "login" ? "bad login" : "newuser", field == "email" ? "not-an-email" : "new@example.com", password);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string body = await response.Content.ReadAsStringAsync();
        Assert.Contains(expected, body);
        Assert.DoesNotContain(password, body);
        Assert.DoesNotContain("SqlException", body);
        using var scope = api.Host.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.ToListAsync());
    }

    [Theory]
    [InlineData("Login", "")]
    [InlineData("Email", "")]
    [InlineData("Password", "")]
    [InlineData("Login", "long")]
    [InlineData("Email", "long")]
    [InlineData("Password", "long")]
    public async Task RequiredFieldsAndLengthLimitsReturn400(string field, string value)
    {
        await using var api = await RegistrationApi.CreateAsync();
        var body = new Dictionary<string, string> { ["Login"] = "newuser", ["Email"] = "new@example.com", ["Password"] = Password };
        body[field] = value == "long" ? new string('x', 257) : value;
        using var response = await api.Client.PostAsJsonAsync("/api/auth/register", body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DuplicateLoginOrEmailUsesIdentityNormalization(bool login)
    {
        await using var api = await RegistrationApi.CreateAsync();
        using var first = await api.RegisterAsync("first", "first@example.com");
        Assert.Equal(HttpStatusCode.Created, first.StatusCode);
        using var duplicate = await api.RegisterAsync(login ? "FIRST" : "second", login ? "second@example.com" : "FIRST@EXAMPLE.COM");
        Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        Assert.Contains(login ? "DuplicateUserName" : "DuplicateEmail", await duplicate.Content.ReadAsStringAsync());
        using var scope = api.Host.Services.CreateScope();
        Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
    }

    [Theory]
    [InlineData(true, 2601)]
    [InlineData(false, 2627)]
    public async Task ConcurrentRegistrationsHandleSqlUniqueConflictsWithoutRetryOr500(bool sameLogin, int sqlError)
    {
        await using var api = await RegistrationApi.CreateAsync();
        api.Writes.SynchronizeTwoRegistrations = true;
        api.Writes.SqlError = sqlError;
        // SQLite in memory enforces the real unique indexes. Only its provider exception is translated
        // to SQL Server's equivalent 2601/2627 so the production SQL Server error path is tested offline.
        var responses = await Task.WhenAll(api.RegisterAsync("first", "first@example.com"),
            api.RegisterAsync(sameLogin ? "FIRST" : "second", sameLogin ? "second@example.com" : "FIRST@EXAMPLE.COM"));
        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.Created);
            var rejected = Assert.Single(responses, response => response.StatusCode == HttpStatusCode.BadRequest);
            string body = await rejected.Content.ReadAsStringAsync();
            Assert.Contains("DuplicateAccount", body);
            Assert.DoesNotContain("SQL-secret", body);
            Assert.DoesNotContain("SqlException", body);
            Assert.Equal(2, api.Writes.Attempts);
            using var scope = api.Host.Services.CreateScope();
            Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
        }
        finally { foreach (var response in responses) response.Dispose(); }
    }

    [Fact]
    public async Task RegistrationLimitReturns429AndDoesNotConsumeLoginLimit()
    {
        await using var api = await RegistrationApi.CreateAsync();
        using (var scope = api.Host.Services.CreateScope())
            Assert.True((await scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>().CreateAsync(new ApplicationUser { UserName = "existing", Email = "existing@example.com" }, Password)).Succeeded);
        for (int i = 0; i < 5; i++)
        {
            using var invalid = await api.RegisterAsync("newuser", "new@example.com", "weak");
            Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        }
        using var limited = await api.RegisterAsync("newuser", "new@example.com");
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var login = await api.Client.PostAsJsonAsync("/api/auth/login", new { Login = "existing", Password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var scopeAfter = api.Host.Services.CreateScope();
        Assert.Equal(1, await scopeAfter.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync());
    }

    [Fact]
    public void EmailMigrationOnlyReplacesItsIndexAndKeepsTheLoginIndex()
    {
        using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=offline.invalid;Database=ModelOnly;Integrated Security=true").Options);
        var user = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(ApplicationUser))!;
        var email = Assert.Single(user.GetIndexes(), index => index.GetDatabaseName() == "EmailIndex");
        Assert.True(email.IsUnique);
        Assert.Equal("[NormalizedEmail] IS NOT NULL", email.GetFilter());
        Assert.Equal("NormalizedEmail", Assert.Single(email.Properties).Name);
        Assert.True(Assert.Single(user.GetIndexes(), index => index.GetDatabaseName() == "UserNameIndex").IsUnique);
        var migration = new AddUniqueNormalizedEmail();
        foreach (var operations in new[] { migration.UpOperations, migration.DownOperations })
        {
            Assert.Equal(2, operations.Count);
            Assert.Equal("EmailIndex", Assert.IsType<DropIndexOperation>(operations[0]).Name);
            var index = Assert.IsType<CreateIndexOperation>(operations[1]);
            Assert.Equal("AspNetUsers", index.Table);
            Assert.Equal(new[] { "NormalizedEmail" }, index.Columns);
        }
        Assert.True(Assert.IsType<CreateIndexOperation>(migration.UpOperations[1]).IsUnique);
        Assert.False(Assert.IsType<CreateIndexOperation>(migration.DownOperations[1]).IsUnique);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task ExistingOperatorEndpointStillRejectsAnonymousRequests()
    {
        await using var api = await RegistrationApi.CreateAsync();
        using var response = await api.Client.PostAsJsonAsync("/api/users", new { Login = "new", Email = "new@example.com", Password });
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        using var scope = api.Host.Services.CreateScope();
        Assert.Empty(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.ToListAsync());
    }

    [Fact]
    public async Task ExhaustedLoginLimitDoesNotBlockRegistration()
    {
        await using var api = await RegistrationApi.CreateAsync();
        for (int i = 0; i < 6; i++)
        {
            using var login = await api.Client.PostAsJsonAsync("/api/auth/login", new { Login = "missing", Password });
            Assert.Equal(i < 5 ? HttpStatusCode.Unauthorized : HttpStatusCode.TooManyRequests, login.StatusCode);
        }
        using var response = await api.RegisterAsync("newuser", "new@example.com");
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }
    private const string Password = "Registration-Password-123!";

    private sealed class RegistrationApi : IAsyncDisposable
    {
        private readonly SqliteConnection _connection = new("Data Source=:memory:");
        public IHost Host { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public RegistrationWrites Writes { get; } = new();
        public static async Task<RegistrationApi> CreateAsync()
        {
            var api = new RegistrationApi();
            await api._connection.OpenAsync();
            var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
            var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:DefaultConnection"] = "Server=offline.invalid;Database=Unused;Integrated Security=true",
                ["Jwt:Issuer"] = "registration-tests", ["Jwt:Audience"] = "registration-tests", ["Jwt:SigningKey"] = key, ["Jwt:ExpirationMinutes"] = "5"
            }).Build();
            api.Host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddDatabaseInfrastructure(configuration);
                    services.AddScoped(_ => new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(api._connection).AddInterceptors(api.Writes).Options));
                    services.AddControllers().AddApplicationPart(typeof(AuthController).Assembly);
                    services.AddApplicationOperatorAuthorization();
                    services.AddRegistrationRateLimiting();
                    services.AddRateLimiter(options => options.AddFixedWindowLimiter("login", limiter => { limiter.PermitLimit = 5; limiter.Window = TimeSpan.FromMinutes(1); limiter.QueueLimit = 0; }));
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidIssuer = "registration-tests", ValidAudience = "registration-tests", IssuerSigningKey = new SymmetricSecurityKey(Convert.FromBase64String(key)), ValidateIssuerSigningKey = true, ValidateLifetime = true
                    });
                }).Configure(app =>
                {
                    app.UseRouting(); app.UseRateLimiter(); app.UseAuthentication(); app.UseAuthorization(); app.UseEndpoints(endpoints => endpoints.MapControllers());
                })).StartAsync();
            api.Client = api.Host.GetTestClient();
            using var scope = api.Host.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await SqliteTestSchema.CreateAsync(db, api._connection);
            return api;
        }
        public Task<HttpResponseMessage> RegisterAsync(string login, string email, string password = Password) => Client.PostAsJsonAsync("/api/auth/register", new RegisterRequest { Login = login, Email = email, Password = password });
        public async ValueTask DisposeAsync() { Client.Dispose(); Host.Dispose(); await _connection.DisposeAsync(); }
    }

    private sealed class RegistrationWrites : SaveChangesInterceptor
    {
        private readonly SemaphoreSlim _gate = new(1);
        private readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, bool> _owners = new();
        private readonly TaskCompletionSource _bothArrived = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public bool SynchronizeTwoRegistrations { get; set; }
        public int SqlError { get; set; } = 2601;
        private int _attempts;
        public int Attempts => _attempts;
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            int attempt = Interlocked.Increment(ref _attempts);
            if (SynchronizeTwoRegistrations)
            {
                if (attempt == 2) _bothArrived.TrySetResult();
                await _bothArrived.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            await _gate.WaitAsync(cancellationToken);
            _owners.TryAdd(data.Context!.ContextId.InstanceId, true);
            return result;
        }
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData data, int result, CancellationToken cancellationToken = default) { Release(data.Context!); return ValueTask.FromResult(result); }
        public override Task SaveChangesFailedAsync(DbContextErrorEventData data, CancellationToken cancellationToken = default)
        {
            Release(data.Context!);
            if (data.Exception is DbUpdateException { InnerException: SqliteException { SqliteExtendedErrorCode: 2067 } })
                throw new DbUpdateException("SQL-secret", UniqueSqlException(SqlError));
            return Task.CompletedTask;
        }
        private void Release(DbContext context) { if (_owners.TryRemove(context.ContextId.InstanceId, out _)) _gate.Release(); }
        private static SqlException UniqueSqlException(int number)
        {
            var constructor = typeof(SqlError).GetConstructors(BindingFlags.NonPublic | BindingFlags.Instance).First(info => info.GetParameters().Length == 9);
            var error = (SqlError)constructor.Invoke(new object?[] { number, (byte)1, (byte)16, "test", "SQL-secret", "test", 1, Convert.ChangeType(0, constructor.GetParameters()[7].ParameterType, System.Globalization.CultureInfo.InvariantCulture), null });
            var errors = (SqlErrorCollection)Activator.CreateInstance(typeof(SqlErrorCollection), nonPublic: true)!;
            typeof(SqlErrorCollection).GetMethod("Add", BindingFlags.NonPublic | BindingFlags.Instance)!.Invoke(errors, new object[] { error });
            return (SqlException)typeof(SqlException).GetMethod("CreateException", BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(SqlErrorCollection), typeof(string) }, null)!.Invoke(null, new object[] { errors, "test" })!;
        }
    }
}
