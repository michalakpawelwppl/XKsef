using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using xKsef.Api.Authorization;
using xKsef.Api.Controllers;
using xKsef.Application.Credentials;
using xKsef.Application.Invoices;
using xKsef.Application.Tenants;
using xKsef.Contracts.Tenants;
using xKsef.Domain.Invoices;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Credentials;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

public sealed class TenantCreationTests
{
    [Fact]
    public async Task OperatorCreatesActiveTenantAndAdminMembershipForExistingUser()
    {
        await using var api = await TenantApi.CreateAsync();
        using var response = await api.PostAsync(userLogin: api.TargetLogin.ToUpperInvariant());
        Guid tenantId = await ReadTenantIdAsync(response);
        using var scope = api.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Tenant tenant = await db.Tenants.Include(x => x.TaxEntity).SingleAsync(x => x.Id == tenantId);
        Assert.True(tenant.IsActive);
        Assert.Equal(api.Prefix + "-tenant", tenant.Name);
        Assert.Equal(api.Nip, tenant.TaxEntity.Nip);
        Assert.Equal(api.Prefix + "-tax", tenant.TaxEntity.Name);
        UserTenant membership = await db.UserTenants.SingleAsync(x => x.TenantId == tenantId);
        Assert.Equal(api.TargetId, membership.UserId);
        Assert.Equal("Admin", membership.Role);
        Assert.Null(membership.PreferredKsefCredentialId);
        var users = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
        Assert.Empty(await users.GetRolesAsync((await users.FindByIdAsync(api.TargetId))!));
        Assert.Equal(1, api.Interceptor.TenantSaveCalls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExistingTaxEntityIsReusedWithoutChangingName(bool sameUser)
    {
        await using var api = await TenantApi.CreateAsync();
        using var firstResponse = await api.PostAsync(name: api.Prefix + "-first");
        Guid firstId = await ReadTenantIdAsync(firstResponse);
        using var secondResponse = await api.PostAsync(name: api.Prefix + "-second", taxEntityName: api.Prefix + "-replacement",
            userLogin: sameUser ? api.TargetLogin : api.OtherLogin);
        Guid secondId = await ReadTenantIdAsync(secondResponse);
        Assert.NotEqual(firstId, secondId);

        using var scope = api.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Tenant first = await db.Tenants.SingleAsync(x => x.Id == firstId);
        Tenant second = await db.Tenants.SingleAsync(x => x.Id == secondId);
        Assert.Equal(first.TaxEntityId, second.TaxEntityId);
        TaxEntity taxEntity = await db.TaxEntities.SingleAsync(x => x.Nip == api.Nip);
        Assert.Equal(api.Prefix + "-tax", taxEntity.Name);
        Assert.Equal(2, await db.UserTenants.CountAsync(x => x.TenantId == firstId || x.TenantId == secondId));
    }

    [Fact]
    public async Task SharedTaxEntityDoesNotShareAccessInvoicesOrPreferences()
    {
        await using var api = await TenantApi.CreateAsync();
        using var firstResponse = await api.PostAsync(name: api.Prefix + "-first");
        Guid firstId = await ReadTenantIdAsync(firstResponse);
        using var secondResponse = await api.PostAsync(name: api.Prefix + "-second", userLogin: api.OtherLogin);
        Guid secondId = await ReadTenantIdAsync(secondResponse);

        using (var scope = api.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Invoices.AddRange(
                new StoredInvoice { TenantId = firstId, KsefNumber = api.Prefix, InvoiceNumber = "first", XmlBytes = [] },
                new StoredInvoice { TenantId = secondId, KsefNumber = api.Prefix, InvoiceNumber = "second", XmlBytes = [] });
            await db.SaveChangesAsync();
            var access = scope.ServiceProvider.GetRequiredService<ITenantAccessService>();
            Assert.True(await access.HasAccessAsync(api.TargetId, firstId));
            Assert.False(await access.HasAccessAsync(api.TargetId, secondId));
            Assert.False(await access.HasAccessAsync(api.OperatorId, firstId));
            Assert.Empty(await access.GetRolesAsync(api.TargetId, secondId));
            var repository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
            Assert.Equal("first", (await repository.GetByKsefNumberAsync(firstId, api.Prefix))!.InvoiceNumber);
            Assert.Equal("second", (await repository.GetByKsefNumberAsync(secondId, api.Prefix))!.InvoiceNumber);
        }

        using var ownList = await api.SendAsync(HttpMethod.Get, "/api/tenants", api.TargetId);
        Assert.Equal(HttpStatusCode.OK, ownList.StatusCode);
        Assert.Equal(firstId, Assert.Single((await ownList.Content.ReadFromJsonAsync<List<TenantDto>>())!).Id);
        using var otherList = await api.SendAsync(HttpMethod.Get, "/api/tenants", api.OtherId);
        Assert.Equal(secondId, Assert.Single((await otherList.Content.ReadFromJsonAsync<List<TenantDto>>())!).Id);
        using var operatorList = await api.SendAsync(HttpMethod.Get, "/api/tenants", api.OperatorId);
        Assert.Empty((await operatorList.Content.ReadFromJsonAsync<List<TenantDto>>())!);

        foreach (HttpMethod method in new[] { HttpMethod.Get, HttpMethod.Put })
        {
            using var own = await api.SendAsync(method, $"/api/tenants/{firstId}/preferred-ksef-credential", api.TargetId,
                method == HttpMethod.Put ? new { CredentialId = (Guid?)null } : null);
            Assert.Equal(method == HttpMethod.Get ? HttpStatusCode.OK : HttpStatusCode.NoContent, own.StatusCode);
            using var foreign = await api.SendAsync(method, $"/api/tenants/{secondId}/preferred-ksef-credential", api.TargetId,
                method == HttpMethod.Put ? new { CredentialId = (Guid?)null } : null);
            Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);
        }

        // Even an actual tenant Admin cannot use the operator endpoint.
        using var denied = await api.PostAsync(callerId: api.TargetId);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        // Existing endpoints also remain available to an ordinary tenant member.
        using (var scope = api.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.UserTenants.SingleAsync(x => x.TenantId == firstId)).Role = "User";
            await db.SaveChangesAsync();
        }
        using var memberRead = await api.SendAsync(HttpMethod.Get, $"/api/tenants/{firstId}/preferred-ksef-credential", api.TargetId);
        Assert.Equal(HttpStatusCode.OK, memberRead.StatusCode);
        using var memberWrite = await api.SendAsync(HttpMethod.Put, $"/api/tenants/{firstId}/preferred-ksef-credential", api.TargetId,
            new { CredentialId = (Guid?)null });
        Assert.Equal(HttpStatusCode.NoContent, memberWrite.StatusCode);
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("other-user", HttpStatusCode.Forbidden)]
    [InlineData("admin-claim", HttpStatusCode.Forbidden)]
    [InlineData("missing-configuration", HttpStatusCode.Forbidden)]
    [InlineData("invalid-configuration", HttpStatusCode.Forbidden)]
    public async Task CreationRequiresConfiguredOperator(string mode, HttpStatusCode expected)
    {
        await using var api = await TenantApi.CreateAsync();
        if (mode == "missing-configuration") api.Configuration["ApplicationOperator:UserId"] = null;
        if (mode == "invalid-configuration") api.Configuration["ApplicationOperator:UserId"] = "invalid";
        string? caller = mode == "anonymous" ? null : mode is "other-user" or "admin-claim" ? api.TargetId : api.OperatorId;
        using var response = await api.SendAsync(HttpMethod.Post, "/api/tenants", caller, api.Body(), mode == "admin-claim");
        Assert.Equal(expected, response.StatusCode);
        await api.AssertNoTenantsAsync();
        Assert.Equal(0, api.Interceptor.TenantSaveCalls);
    }

    [Theory]
    [InlineData("Name", "")]
    [InlineData("Name", " ")]
    [InlineData("Name", "too-long")]
    [InlineData("Nip", "123456789")]
    [InlineData("Nip", "12345678901")]
    [InlineData("Nip", "123456789a")]
    [InlineData("Nip", "１２３４５６７８９０")]
    [InlineData("Nip", "1234567890\n")]
    [InlineData("TaxEntityName", "")]
    [InlineData("TaxEntityName", "too-long")]
    [InlineData("UserLogin", " ")]
    [InlineData("UserLogin", "too-long")]
    [InlineData("UserLogin", "missing-user")]
    public async Task InvalidDataOrMissingUserReturnsBadRequestWithoutPartialData(string field, string value)
    {
        await using var api = await TenantApi.CreateAsync();
        Dictionary<string, object?> body = api.Body();
        body[field] = value == "too-long" ? new string('x', field == "UserLogin" ? 257 : 201) : value;
        using var response = await api.SendAsync(HttpMethod.Post, "/api/tenants", api.OperatorId, body);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await api.AssertNoTenantsAsync();
        Assert.Equal(0, api.Interceptor.TenantSaveCalls);
    }

    [Fact]
    public async Task MissingFieldsAreRejected()
    {
        await using var api = await TenantApi.CreateAsync();
        using var response = await api.SendAsync(HttpMethod.Post, "/api/tenants", api.OperatorId, new { });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await api.AssertNoTenantsAsync();
    }

    [Fact]
    public async Task ClientCannotChooseTenantIdRoleOrActiveFlag()
    {
        await using var api = await TenantApi.CreateAsync();
        Guid suppliedId = Guid.NewGuid();
        Dictionary<string, object?> body = api.Body();
        body["TenantId"] = suppliedId;
        body["Id"] = suppliedId;
        body["Role"] = "User";
        body["IsActive"] = false;
        using var response = await api.SendAsync(HttpMethod.Post, "/api/tenants", api.OperatorId, body);
        Guid createdId = await ReadTenantIdAsync(response);
        Assert.NotEqual(suppliedId, createdId);
        using var scope = api.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True((await db.Tenants.SingleAsync(x => x.Id == createdId)).IsActive);
        Assert.Equal("Admin", (await db.UserTenants.SingleAsync(x => x.TenantId == createdId)).Role);
        Assert.False(await db.Tenants.AnyAsync(x => x.Id == suppliedId));
    }

    [Fact]
    public async Task ConcurrentNewNipReturnsCreatedAndConflictWithoutPartialDataOrRetry()
    {
        await using var api = await TenantApi.CreateAsync();
        api.Interceptor.GateParallelSaves = true;
        HttpResponseMessage[] responses = await Task.WhenAll(
            api.PostAsync(name: api.Prefix + "-first"),
            api.PostAsync(name: api.Prefix + "-second", userLogin: api.OtherLogin)).WaitAsync(TimeSpan.FromSeconds(20));
        try
        {
            Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Created);
            HttpResponseMessage conflict = Assert.Single(responses, x => x.StatusCode == HttpStatusCode.Conflict);
            string error = await conflict.Content.ReadAsStringAsync();
            Assert.DoesNotContain("SqlException", error);
            Assert.DoesNotContain("INSERT", error);
            Assert.Equal(2, api.Interceptor.TenantSaveCalls);

            using var scope = api.Server.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.TaxEntities.CountAsync(x => x.Nip == api.Nip));
            Assert.Equal(1, await db.Tenants.CountAsync(x => x.Name.StartsWith(api.Prefix)));
            Assert.Equal(1, await db.UserTenants.CountAsync(x => x.UserId == api.TargetId || x.UserId == api.OtherId));
        }
        finally
        {
            foreach (HttpResponseMessage response in responses) response.Dispose();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MembershipInsertFailureRollsBackAllNewEntities(bool existingTaxEntity)
    {
        await using var api = await TenantApi.CreateAsync();
        if (existingTaxEntity)
        {
            using var setup = api.Server.Services.CreateScope();
            var db = setup.ServiceProvider.GetRequiredService<AppDbContext>();
            db.TaxEntities.Add(new TaxEntity { Id = Guid.NewGuid(), Nip = api.Nip, Name = api.Prefix + "-original" });
            await db.SaveChangesAsync();
        }

        api.Interceptor.FailMembership = true;
        using (var scope = api.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.True(db.Database.IsSqlServer());
            Assert.Equal(AutoTransactionBehavior.WhenNeeded, db.Database.AutoTransactionBehavior);
            Assert.Null(db.Database.CurrentTransaction);
            var service = scope.ServiceProvider.GetRequiredService<ITenantService>();
            DbUpdateException exception = await Assert.ThrowsAsync<DbUpdateException>(() =>
                service.CreateAsync(api.Prefix + "-tenant", api.Nip, api.Prefix + "-tax", api.TargetLogin));
            Assert.Equal(547, Assert.IsType<SqlException>(exception.InnerException).Number);
        }

        using var verification = api.Server.Services.CreateScope();
        var verificationDb = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await verificationDb.Tenants.AnyAsync(x => x.Name.StartsWith(api.Prefix)));
        Assert.False(await verificationDb.UserTenants.AnyAsync(x => x.UserId == api.TargetId));
        TaxEntity? taxEntity = await verificationDb.TaxEntities.SingleOrDefaultAsync(x => x.Nip == api.Nip);
        if (existingTaxEntity)
            Assert.Equal(api.Prefix + "-original", Assert.IsType<TaxEntity>(taxEntity).Name);
        else
            Assert.Null(taxEntity);
        Assert.Equal(1, api.Interceptor.TenantSaveCalls);
    }

    private static async Task<Guid> ReadTenantIdAsync(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Single(body.RootElement.EnumerateObject());
        Guid tenantId = body.RootElement.GetProperty("tenantId").GetGuid();
        Assert.NotEqual(Guid.Empty, tenantId);
        return tenantId;
    }

    private sealed class TenantSaveInterceptor : SaveChangesInterceptor
    {
        private readonly TaskCompletionSource _bothReady = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _tenantSaveCalls;
        public int TenantSaveCalls => Volatile.Read(ref _tenantSaveCalls);
        public bool GateParallelSaves { get; set; }
        public bool FailMembership { get; set; }

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            DbContext db = eventData.Context!;
            if (!db.ChangeTracker.Entries<Tenant>().Any(x => x.State == EntityState.Added)) return result;
            int calls = Interlocked.Increment(ref _tenantSaveCalls);
            Assert.Equal(AutoTransactionBehavior.WhenNeeded, db.Database.AutoTransactionBehavior);
            if (FailMembership)
                db.ChangeTracker.Entries<UserTenant>().Single(x => x.State == EntityState.Added).Entity.UserId = Guid.NewGuid().ToString();
            if (GateParallelSaves)
            {
                Assert.Single(db.ChangeTracker.Entries<TaxEntity>(), x => x.State == EntityState.Added);
                if (calls == 2) _bothReady.TrySetResult();
                await _bothReady.Task.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken);
            }
            return result;
        }
    }

    private sealed class TenantApi : IAsyncDisposable
    {
        private readonly byte[] _signingKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        private IHost _host = null!;
        public string Prefix { get; } = "tenant-test-" + Guid.NewGuid().ToString("N");
        public string Nip { get; } = Random.Shared.NextInt64(1_000_000_000L, 10_000_000_000L).ToString(System.Globalization.CultureInfo.InvariantCulture);
        public string OperatorId { get; } = Guid.NewGuid().ToString();
        public string TargetId { get; } = Guid.NewGuid().ToString();
        public string OtherId { get; } = Guid.NewGuid().ToString();
        public string TargetLogin => Prefix + "-target";
        public string OtherLogin => Prefix + "-other";
        public IConfigurationRoot Configuration { get; private set; } = null!;
        public TestServer Server { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public TenantSaveInterceptor Interceptor { get; } = new();

        public static async Task<TenantApi> CreateAsync()
        {
            var api = new TenantApi();
            api.Configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile("appsettings.Test.json", optional: false)
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ApplicationOperator:UserId"] = api.OperatorId,
                    ["Jwt:Issuer"] = "xksef-tenant-tests",
                    ["Jwt:Audience"] = "xksef-tenant-tests",
                    ["Jwt:SigningKey"] = Convert.ToBase64String(api._signingKey),
                    ["Jwt:ExpirationMinutes"] = "5"
                }).Build();

            api._host = await new HostBuilder().ConfigureWebHost(webHost => webHost.UseTestServer()
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(api.Configuration);
                    services.AddDatabaseInfrastructure(api.Configuration);
                    services.AddDbContext<AppDbContext>(options => options.AddInterceptors(api.Interceptor));
                    services.AddHttpClient();
                    services.AddScoped<ICredentialServiceClient, CredentialServiceClient>();
                    services.AddControllers().AddApplicationPart(typeof(TenantsController).Assembly);
                    services.AddApplicationOperatorAuthorization();
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
                    {
                        options.TokenValidationParameters = new TokenValidationParameters
                        {
                            ValidateIssuer = true, ValidIssuer = "xksef-tenant-tests",
                            ValidateAudience = true, ValidAudience = "xksef-tenant-tests",
                            ValidateLifetime = true, RequireExpirationTime = true,
                            ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(api._signingKey),
                            RequireSignedTokens = true, ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero
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
                foreach ((string id, string login) in new[]
                {
                    (api.OperatorId, api.Prefix + "-operator"), (api.TargetId, api.TargetLogin), (api.OtherId, api.OtherLogin)
                })
                {
                    IdentityResult result = await users.CreateAsync(new ApplicationUser { Id = id, UserName = login, Email = login + "@example.com" });
                    Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(x => x.Code)));
                }
                return api;
            }
            catch
            {
                await api.DisposeAsync();
                throw;
            }
        }

        public Dictionary<string, object?> Body(string? name = null, string? taxEntityName = null, string? userLogin = null) => new()
        {
            ["Name"] = name ?? Prefix + "-tenant",
            ["Nip"] = Nip,
            ["TaxEntityName"] = taxEntityName ?? Prefix + "-tax",
            ["UserLogin"] = userLogin ?? TargetLogin
        };

        public Task<HttpResponseMessage> PostAsync(string? name = null, string? taxEntityName = null, string? userLogin = null, string? callerId = null)
            => SendAsync(HttpMethod.Post, "/api/tenants", callerId ?? OperatorId, Body(name, taxEntityName, userLogin));

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string url, string? callerId, object? body = null, bool admin = false)
        {
            using var request = new HttpRequestMessage(method, url);
            if (callerId is not null)
            {
                var claims = new List<Claim> { new(JwtRegisteredClaimNames.Sub, callerId) };
                if (admin) claims.Add(new Claim(ClaimTypes.Role, "Admin"));
                var token = new JwtSecurityToken(issuer: "xksef-tenant-tests", audience: "xksef-tenant-tests", claims: claims,
                    expires: DateTime.UtcNow.AddMinutes(5),
                    signingCredentials: new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            }
            if (body is not null) request.Content = JsonContent.Create(body);
            return await Client.SendAsync(request);
        }

        public async Task AssertNoTenantsAsync()
        {
            using var scope = Server.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Tenants.AnyAsync(x => x.Name.StartsWith(Prefix)));
            Assert.False(await db.TaxEntities.AnyAsync(x => x.Nip == Nip));
            Assert.False(await db.UserTenants.AnyAsync(x => x.UserId == TargetId || x.UserId == OtherId));
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                using var scope = Server.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                await using var transaction = await db.Database.BeginTransactionAsync();
                var tenantIds = db.Tenants.Where(x => x.Name.StartsWith(Prefix)).Select(x => x.Id);
                await db.Invoices.Where(x => tenantIds.Contains(x.TenantId)).ExecuteDeleteAsync();
                await db.Tenants.Where(x => x.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
                await db.TaxEntities.Where(x => x.Name.StartsWith(Prefix)).ExecuteDeleteAsync();
                await db.Users.Where(x => x.UserName != null && x.UserName.StartsWith(Prefix)).ExecuteDeleteAsync();
                await transaction.CommitAsync();
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
