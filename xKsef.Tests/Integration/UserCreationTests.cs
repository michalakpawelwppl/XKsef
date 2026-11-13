using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Hosting;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using xKsef.Api.Authorization;
using xKsef.Api.Controllers;
using xKsef.Application.Authentication;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

public sealed class UserCreationTests
{
    [Fact]
    public async Task AnonymousRequestIsUnauthorized()
    {
        await using var api = await UserApi.CreateAsync();
        using var response = await api.CreateUserAsync(null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        await api.AssertOnlyOperatorExistsAsync();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OtherUserIsForbiddenRegardlessOfAdminRole(bool admin)
    {
        await using var api = await UserApi.CreateAsync();
        using var response = await api.CreateUserAsync(api.CreateToken(Guid.NewGuid().ToString(), admin));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await api.AssertOnlyOperatorExistsAsync();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("malformed")]
    [InlineData("padded")]
    [InlineData("control")]
    [InlineData("too-long")]
    public async Task MissingOrInvalidOperatorConfigurationIsForbidden(string? configuration)
    {
        await using var api = await UserApi.CreateAsync();
        api.Configuration["ApplicationOperator:UserId"] = configuration switch
        {
            "malformed" => "not-the-operator",
            "padded" => " " + api.OperatorId + " ",
            "control" => api.OperatorId + "\n",
            "too-long" => new string('x', 451),
            _ => configuration
        };
        using var response = await api.CreateUserAsync(api.CreateToken(api.OperatorId));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await api.AssertOnlyOperatorExistsAsync();
    }

    [Fact]
    public async Task MatchingConfigurationAndJwtWithoutExistingAccountIsForbidden()
    {
        await using var api = await UserApi.CreateAsync();
        string missingUserId = Guid.NewGuid().ToString();
        api.Configuration["ApplicationOperator:UserId"] = missingUserId;
        using var response = await api.CreateUserAsync(api.CreateToken(missingUserId));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        await api.AssertOnlyOperatorExistsAsync();
    }

    [Fact]
    public async Task OperatorIdComparisonIsOrdinal()
    {
        await using var api = await UserApi.CreateAsync("Operator-CaseSensitive-" + Guid.NewGuid().ToString("N"));
        using var response = await api.CreateUserAsync(api.CreateToken(api.OperatorId.ToLowerInvariant()));
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OperatorCanCreateUserWithGuidOrOtherStringIdentityKey(bool useStringKey)
    {
        await using var api = await UserApi.CreateAsync(useStringKey ? "operator-" + Guid.NewGuid().ToString("N") : null);
        using var response = await api.CreateUserAsync(api.CreateToken(api.OperatorId));
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(body.RootElement.EnumerateObject());
        string userId = body.RootElement.GetProperty("userId").GetString()!;
        Assert.NotEqual(api.OperatorId, userId);

        using var scope = api.Server.Services.CreateScope();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        ApplicationUser user = Assert.IsType<ApplicationUser>(await users.FindByIdAsync(userId));
        Assert.True(Guid.TryParse(user.Id, out _));
        Assert.True(await users.CheckPasswordAsync(user, UserApi.Password));
        Assert.NotEqual(UserApi.Password, user.PasswordHash);
        Assert.True(user.LockoutEnabled);
        Assert.False(user.EmailConfirmed);
        Assert.Empty(await users.GetRolesAsync(user));
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await dbContext.UserTenants.AnyAsync(x => x.UserId == userId));
        Assert.False(await dbContext.UserClaims.AnyAsync(x => x.UserId == userId));
        Assert.Equal(api.OperatorId, api.Configuration["ApplicationOperator:UserId"]);

        var authentication = scope.ServiceProvider.GetRequiredService<IUserAuthenticationService>();
        Assert.NotNull(await authentication.AuthenticateAsync(api.Prefix + "-new", UserApi.Password));

        using var denied = await api.CreateUserAsync(api.CreateToken(userId), login: api.Prefix + "-another");
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Theory]
    [InlineData("password", "PasswordTooShort")]
    [InlineData("login", "InvalidUserName")]
    [InlineData("email", "InvalidEmail")]
    [InlineData("duplicate-login", "DuplicateUserName")]
    [InlineData("duplicate-email", "DuplicateEmail")]
    public async Task IdentityValidationReturnsBadRequestWithoutCreatingAccount(string invalidField, string expectedCode)
    {
        await using var api = await UserApi.CreateAsync();
        string login = invalidField switch
        {
            "login" => api.Prefix + " invalid",
            "duplicate-login" => api.Prefix.ToUpperInvariant() + "-OPERATOR",
            _ => api.Prefix + "-new"
        };
        string email = invalidField switch
        {
            "email" => "invalid-email",
            "duplicate-email" => api.Prefix.ToUpperInvariant() + "-OPERATOR@EXAMPLE.COM",
            _ => api.Prefix + "-new@example.com"
        };
        string password = invalidField == "password" ? "weak" : UserApi.Password;
        using var response = await api.CreateUserAsync(api.CreateToken(api.OperatorId), login, email, password);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        string json = await response.Content.ReadAsStringAsync();
        using JsonDocument body = JsonDocument.Parse(json);
        string[] codes = body.RootElement.GetProperty("errors").GetProperty("Identity").EnumerateArray().Select(x => x.GetString()!).ToArray();
        Assert.Contains(expectedCode, codes);
        Assert.DoesNotContain(password, json, StringComparison.Ordinal);
        Assert.DoesNotContain(login, json, StringComparison.Ordinal);
        Assert.DoesNotContain(email, json, StringComparison.Ordinal);
        await api.AssertOnlyOperatorExistsAsync();
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"login\":\"\",\"email\":\"\",\"password\":\"\"}")]
    [InlineData("{\"login\":\"user\",\"email\":\"user@example.com\"}")]
    [InlineData("{")]
    public async Task MissingOrMalformedInputReturnsBadRequest(string json)
    {
        await using var api = await UserApi.CreateAsync();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", api.CreateToken(api.OperatorId));
        request.Content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");
        using var response = await api.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await api.AssertOnlyOperatorExistsAsync();
    }

    [Fact]
    public async Task EnvironmentVariableOverridesOperatorConfiguration()
    {
        string environmentPrefix = "XKSEF_TEST_" + Guid.NewGuid().ToString("N") + "_";
        string variableName = environmentPrefix + "ApplicationOperator__UserId";
        string operatorId = "operator-" + Guid.NewGuid().ToString("N");
        Environment.SetEnvironmentVariable(variableName, operatorId);
        try
        {
            await using var api = await UserApi.CreateAsync(operatorId, environmentPrefix);
            Assert.Equal(operatorId, api.Configuration["ApplicationOperator:UserId"]);
            using var response = await api.CreateUserAsync(api.CreateToken(operatorId));
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable(variableName, null);
        }
    }

    private sealed class UserApi : IAsyncDisposable
    {
        public const string Password = "Test-Password-123!";
        private readonly byte[] _signingKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        public string Prefix { get; } = "test-" + Guid.NewGuid().ToString("N");
        public string OperatorId { get; }
        public IConfigurationRoot Configuration { get; private set; } = null!;
        private IHost _host = null!;
        public TestServer Server { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;

        private UserApi(string? operatorId)
        {
            OperatorId = operatorId ?? new ApplicationUser().Id;
        }

        public static async Task<UserApi> CreateAsync(string? operatorId = null, string? environmentPrefix = null)
        {
            var api = new UserApi(operatorId);
            var configuration = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.Test.json", optional: false)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApplicationOperator:UserId"] = environmentPrefix is null ? api.OperatorId : "overridden",
                    ["Jwt:Issuer"] = "xksef-user-tests",
                    ["Jwt:Audience"] = "xksef-user-tests",
                    ["Jwt:SigningKey"] = Convert.ToBase64String(api._signingKey),
                    ["Jwt:ExpirationMinutes"] = "5"
                });
            if (environmentPrefix is not null)
                configuration.AddEnvironmentVariables(environmentPrefix);
            api.Configuration = configuration.Build();

            api._host = await new HostBuilder().ConfigureWebHost(webHost => webHost.UseTestServer()
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(api.Configuration);
                    services.AddDatabaseInfrastructure(api.Configuration);
                    services.AddControllers().AddApplicationPart(typeof(UsersController).Assembly);
                    services.AddApplicationOperatorAuthorization();
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true,
                            ValidIssuer = "xksef-user-tests",
                            ValidateAudience = true,
                            ValidAudience = "xksef-user-tests",
                            ValidateLifetime = true,
                            RequireExpirationTime = true,
                            ValidateIssuerSigningKey = true,
                            IssuerSigningKey = new SymmetricSecurityKey(api._signingKey),
                            RequireSignedTokens = true,
                            ValidAlgorithms = [SecurityAlgorithms.HmacSha256],
                            ClockSkew = TimeSpan.Zero
                        };
                    });
                })
                .Configure(app =>
                {
                    app.UseRouting();
                    app.UseAuthentication();
                    app.UseAuthorization();
                    app.UseEndpoints(endpoints => endpoints.MapControllers());
                })).StartAsync();
            api.Server = api._host.GetTestServer();
            api.Client = api.Server.CreateClient();

            try
            {
                using var scope = api.Server.Services.CreateScope();
                var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
                IdentityResult result = await users.CreateAsync(new ApplicationUser
                {
                    Id = api.OperatorId,
                    UserName = api.Prefix + "-operator",
                    Email = api.Prefix + "-operator@example.com"
                });
                Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(x => x.Code)));
                return api;
            }
            catch
            {
                await api.DisposeAsync();
                throw;
            }
        }

        public string CreateToken(string userId, bool admin = false)
        {
            var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, userId) };
            if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
            var token = new JwtSecurityToken(
                issuer: "xksef-user-tests",
                audience: "xksef-user-tests",
                claims: claims,
                expires: DateTime.UtcNow.AddMinutes(5),
                signingCredentials: new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256));
            return new JwtSecurityTokenHandler().WriteToken(token);
        }

        public async Task<HttpResponseMessage> CreateUserAsync(string? token, string? login = null, string? email = null, string? password = null)
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, "/api/users");
            if (token is not null)
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Content = JsonContent.Create(new
            {
                Login = login ?? Prefix + "-new",
                Email = email ?? Prefix + "-new@example.com",
                Password = password ?? Password
            });
            return await Client.SendAsync(request);
        }

        public async Task AssertOnlyOperatorExistsAsync()
        {
            using var scope = Server.Services.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await dbContext.Users.CountAsync(x => x.UserName != null && x.UserName.StartsWith(Prefix)));
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                using var scope = Server.Services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await dbContext.Users.Where(x => x.UserName != null && x.UserName.StartsWith(Prefix)).ExecuteDeleteAsync();
            }
            finally
            {
                Client.Dispose();
                _host.Dispose();
                (Configuration as IDisposable)?.Dispose();
            }
        }
    }
}
