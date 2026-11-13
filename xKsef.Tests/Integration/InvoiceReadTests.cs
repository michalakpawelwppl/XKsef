using System.Collections.Concurrent;
using System.Data.Common;
using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Globalization;
using xKsef.Contracts.Invoices;
using xKsef.Domain.Invoices;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.IdentityModel.Tokens;
using xKsef.Api.Authorization;
using xKsef.Api.Controllers;
using xKsef.Application.Invoices;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Ksef.Invoices;
using xKsef.Infrastructure.Persistence;
using xKsef.Tests.Infrastructure;
using Direction = xKsef.Domain.Invoices.InvoiceDirection;

namespace xKsef.Tests.Integration;

public sealed class InvoiceReadTests
{
    [Theory]
    [InlineData("values")]
    [InlineData("zero")]
    [InlineData("null")]
    public async Task ApiPreservesTheExtendedContractAndSeparatesIdenticalKsefNumbers(string variant)
    {
        await using var api = await InvoiceApi.CreateAsync();
        var first = InvoiceReadTestData.Create(api.TenantA, api.Prefix, variant);
        var second = InvoiceReadTestData.Create(api.TenantB, api.Prefix, variant == "values" ? "zero" : "values");
        using (var scope = api.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Invoices.AddRange(first, second);
            await db.SaveChangesAsync();
        }

        using var own = await api.GetAsync(api.UserA, api.TenantA.ToString());
        Assert.Equal(HttpStatusCode.OK, own.StatusCode);
        Assert.Equal("application/json", own.Content.Headers.ContentType!.MediaType);
        using var ownJson = JsonDocument.Parse(await own.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, ownJson.RootElement.ValueKind);
        InvoiceReadTestData.AssertJson(Assert.Single(ownJson.RootElement.EnumerateArray()), first);

        using var other = await api.GetAsync(api.UserB, api.TenantB.ToString());
        Assert.Equal(HttpStatusCode.OK, other.StatusCode);
        using var otherJson = JsonDocument.Parse(await other.Content.ReadAsStringAsync());
        InvoiceReadTestData.AssertJson(Assert.Single(otherJson.RootElement.EnumerateArray()), second);
        AssertInvoiceQueriesDoNotReadXml(api);
    }

    [Fact]
    public async Task RepositoryAndApiKeepTenantIssueDateDirectionAndSortFiltersWithoutLoadingXml()
    {
        await using var api = await InvoiceApi.CreateAsync();
        var first = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-first");
        var last = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-last");
        last.IssueDate = new DateOnly(2026, 11, 13);
        var before = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-before");
        before.IssueDate = new DateOnly(2026, 11, 11);
        var after = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-after");
        after.IssueDate = new DateOnly(2026, 11, 14);
        var purchase = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-purchase");
        purchase.Direction = Direction.Purchase;
        var foreign = InvoiceReadTestData.Create(api.TenantB, first.KsefNumber);
        using (var scope = api.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Invoices.AddRange(first, last, before, after, purchase, foreign);
            await db.SaveChangesAsync();
        }
        DateOnly from = new(2026, 11, 12);
        DateOnly to = new(2026, 11, 13);
        using (var scope = api.Server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var repository = scope.ServiceProvider.GetRequiredService<IInvoiceRepository>();
            var result = await repository.GetInvoicesAsync(api.TenantA, from, to, Direction.Sales);
            Assert.Equal(new[] { last.KsefNumber, first.KsefNumber }, result.Select(x => x.KsefNumber));
            Assert.All(result, invoice =>
            {
                Assert.Equal(api.TenantA, invoice.TenantId);
                Assert.Equal(Direction.Sales, invoice.Direction);
                Assert.Empty(invoice.XmlBytes);
            });
            Assert.Empty(db.ChangeTracker.Entries());
            var purchases = await repository.GetInvoicesAsync(api.TenantA, from, to, Direction.Purchase);
            Assert.Equal(purchase.KsefNumber, Assert.Single(purchases).KsefNumber);
        }
        using var response = await api.GetAsync(api.UserA, api.TenantA.ToString(), "from=2026-11-12&to=2026-11-13&direction=Sales");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var invoices = document.RootElement.EnumerateArray().ToArray();
        Assert.Equal(2, invoices.Length);
        InvoiceReadTestData.AssertJson(invoices[0], last);
        InvoiceReadTestData.AssertJson(invoices[1], first);
        AssertInvoiceQueriesDoNotReadXml(api);
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("foreign-tenant", HttpStatusCode.Forbidden)]
    [InlineData("unknown-user", HttpStatusCode.Forbidden)]
    [InlineData("missing-header", HttpStatusCode.BadRequest)]
    [InlineData("invalid-header", HttpStatusCode.BadRequest)]
    public async Task ExistingEndpointAuthorizationRemainsEffective(string mode, HttpStatusCode expected)
    {
        await using var api = await InvoiceApi.CreateAsync();
        string? userId = mode == "anonymous" ? null : mode == "unknown-user" ? Guid.NewGuid().ToString() : api.UserA;
        string? tenantId = mode switch
        {
            "foreign-tenant" => api.TenantB.ToString(),
            "missing-header" => null,
            "invalid-header" => "invalid",
            _ => api.TenantA.ToString()
        };
        using var response = await api.GetAsync(userId, tenantId);
        Assert.Equal(expected, response.StatusCode);
        Assert.DoesNotContain(api.Sql.Commands, sql => sql.Contains("FROM [invoices]", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("from=2026-11-14&to=2026-11-12&direction=Sales")]
    [InlineData("from=2026-11-12&to=2026-11-14&direction=999")]
    public async Task ExistingQueryValidationRemainsEffective(string query)
    {
        await using var api = await InvoiceApi.CreateAsync();
        using var response = await api.GetAsync(api.UserA, api.TenantA.ToString(), query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.DoesNotContain(api.Sql.Commands, sql => sql.Contains("FROM [invoices]", StringComparison.Ordinal));
    }

    [Fact]
    public async Task QueryDefaultsToInclusiveIssueDatesAndKeepsTheLegacyEndpointUnchanged()
    {
        await using var api = await InvoiceApi.CreateAsync();
        var first = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-first");
        first.Currency = "PLN"; first.NetAmount = 100m; first.VatAmount = 23m; first.GrossAmount = 123m;
        var last = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-last");
        last.IssueDate = new DateOnly(2026, 11, 13);
        last.Currency = "PLN"; last.NetAmount = -40m; last.VatAmount = -9.2m; last.GrossAmount = -49.2m;
        var before = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-before");
        before.IssueDate = new DateOnly(2026, 11, 11);
        var after = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-after");
        after.IssueDate = new DateOnly(2026, 11, 14);
        var purchase = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-purchase");
        purchase.Direction = Direction.Purchase;
        var foreign = InvoiceReadTestData.Create(api.TenantB, first.KsefNumber);
        await AddQueryInvoices(api, first, last, before, after, purchase, foreign);

        const string query = "from=2026-11-12&to=2026-11-13&direction=Sales";
        using var response = await api.GetAsync(api.UserA, api.TenantA.ToString(), query, queryEndpoint: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(new[] { "items", "summaries" }, json.RootElement.EnumerateObject().Select(property => property.Name));
        var result = JsonSerializer.Deserialize<InvoiceQueryResultDto>(json, JsonSerializerOptions.Web)!;
        Assert.Equal(new[] { last.KsefNumber, first.KsefNumber }, result.Items.Select(invoice => invoice.KsefNumber));
        Assert.Equal(new InvoiceCurrencySummaryDto("PLN", 2, 60m, 13.8m, 73.8m, 0, 0), Assert.Single(result.Summaries));
        Assert.Single(InvoiceQueries(api));
        AssertInvoiceQueriesDoNotReadXml(api);

        using var legacy = await api.GetAsync(api.UserA, api.TenantA.ToString(), query + "&dateKind=InvoicingDate&timeZoneId=Invalid/Zone");
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode);
        using var oldJson = JsonDocument.Parse(await legacy.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, oldJson.RootElement.ValueKind);
        Assert.Equal(oldJson.RootElement.GetRawText(), json.RootElement.GetProperty("items").GetRawText());
    }

    [Theory]
    [InlineData("2026-03-29", "Europe/Warsaw", "2026-03-28T23:00:00Z", "2026-03-29T22:00:00Z")]
    [InlineData("2026-10-25", "Europe/Warsaw", "2026-10-24T22:00:00Z", "2026-10-25T23:00:00Z")]
    [InlineData("2026-07-15", "Asia/Kathmandu", "2026-07-14T18:15:00Z", "2026-07-15T18:15:00Z")]
    public async Task QueryFiltersInvoicingInstantsInSqlWithoutFallbackOrXml(string day, string zone, string startText, string endText)
    {
        await using var api = await InvoiceApi.CreateAsync();
        DateTimeOffset start = DateTimeOffset.Parse(startText, CultureInfo.InvariantCulture);
        DateTimeOffset end = DateTimeOffset.Parse(endText, CultureInfo.InvariantCulture);
        var first = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-start");
        first.InvoicingDate = start.ToOffset(TimeSpan.FromHours(5.75));
        first.NetAmount = 10m; first.VatAmount = 1m; first.GrossAmount = 11m; first.VatAmountPln = 999m;
        var last = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-last");
        last.InvoicingDate = end.AddTicks(-1).ToOffset(TimeSpan.FromHours(-7));
        last.NetAmount = -5m; last.VatAmount = -0.5m; last.GrossAmount = -5.5m;
        var before = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-before");
        before.InvoicingDate = start.AddTicks(-1);
        var after = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-end");
        after.InvoicingDate = end.ToOffset(TimeSpan.FromHours(-3.5));
        var absent = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-null");
        absent.InvoicingDate = null;
        absent.IssueDate = DateOnly.Parse(day, CultureInfo.InvariantCulture);
        var purchase = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-purchase");
        purchase.InvoicingDate = start;
        purchase.Direction = Direction.Purchase;
        var foreign = InvoiceReadTestData.Create(api.TenantB, first.KsefNumber);
        foreign.InvoicingDate = start;
        await AddQueryInvoices(api, first, last, before, after, absent, purchase, foreign);
        string query = $"from={day}&to={day}&direction=Sales&dateKind=InvoicingDate&timeZoneId={Uri.EscapeDataString(zone)}";
        using var response = await api.GetAsync(api.UserA, api.TenantA.ToString(), query, queryEndpoint: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<InvoiceQueryResultDto>())!;
        Assert.Equal(new[] { last.KsefNumber, first.KsefNumber }, result.Items.Select(invoice => invoice.KsefNumber));
        Assert.Equal(new InvoiceCurrencySummaryDto("EUR", 2, 5m, 0.5m, 5.5m, 0, 0), Assert.Single(result.Summaries));
        foreach (var expected in new[] { first, last })
        {
            var actual = result.Items.Single(invoice => invoice.KsefNumber == expected.KsefNumber);
            Assert.Equal(expected.InvoicingDate, actual.InvoicingDate);
            Assert.Equal(expected.InvoicingDate!.Value.Offset, actual.InvoicingDate!.Value.Offset);
            Assert.Equal(expected.InvoicingDate.Value.Ticks, actual.InvoicingDate.Value.Ticks);
        }
        string sql = Assert.Single(InvoiceQueries(api));
        Assert.Contains("[InvoicingDate] >=", sql);
        Assert.Contains("[InvoicingDate] <", sql);
        Assert.Contains("[TenantId] =", sql);
        Assert.Contains("[Direction] =", sql);
        Assert.DoesNotContain("CONVERT(date", sql, StringComparison.OrdinalIgnoreCase);
        AssertInvoiceQueriesDoNotReadXml(api);

        using var purchaseResponse = await api.GetAsync(api.UserA, api.TenantA.ToString(), query.Replace("direction=Sales", "direction=Purchase"), queryEndpoint: true);
        Assert.Equal(HttpStatusCode.OK, purchaseResponse.StatusCode);
        var purchases = (await purchaseResponse.Content.ReadFromJsonAsync<InvoiceQueryResultDto>())!;
        Assert.Equal(purchase.KsefNumber, Assert.Single(purchases.Items).KsefNumber);
        Assert.Equal(purchase.NetAmount, Assert.Single(purchases.Summaries).NetTotal);

        using var foreignResponse = await api.GetAsync(api.UserB, api.TenantB.ToString(), query, queryEndpoint: true);
        Assert.Equal(HttpStatusCode.OK, foreignResponse.StatusCode);
        var otherTenant = (await foreignResponse.Content.ReadFromJsonAsync<InvoiceQueryResultDto>())!;
        Assert.Equal(first.KsefNumber, Assert.Single(otherTenant.Items).KsefNumber);
        Assert.Equal(foreign.NetAmount, Assert.Single(otherTenant.Summaries).NetTotal);
    }

    [Fact]
    public async Task QuerySummariesDistinguishPartialMissingAndZeroAmountsByCurrency()
    {
        await using var api = await InvoiceApi.CreateAsync();
        var euro = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-euro");
        euro.NetAmount = 100m; euro.VatAmount = 23m; euro.GrossAmount = 123m; euro.VatAmountPln = 999m;
        var partial = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-partial");
        partial.NetAmount = null; partial.VatAmount = 0m; partial.GrossAmount = -23m;
        var zero = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-zero", "zero");
        zero.Currency = "PLN"; zero.GrossAmount = 0m;
        var absent = InvoiceReadTestData.Create(api.TenantA, api.Prefix + "-absent", "null");
        absent.Currency = "USD"; absent.GrossAmount = 0m;
        await AddQueryInvoices(api, euro, partial, zero, absent);
        using var response = await api.GetAsync(api.UserA, api.TenantA.ToString(), queryEndpoint: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<InvoiceQueryResultDto>())!;
        Assert.Equal(4, result.Items.Count);
        Assert.Equal(4, result.Summaries.Sum(summary => summary.InvoiceCount));
        Assert.Equal(new[]
        {
            new InvoiceCurrencySummaryDto("EUR", 2, null, 23m, 100m, 1, 0),
            new InvoiceCurrencySummaryDto("PLN", 1, 0m, 0m, 0m, 0, 0),
            new InvoiceCurrencySummaryDto("USD", 1, null, null, 0m, 1, 1)
        }, result.Summaries);
        Assert.Single(InvoiceQueries(api));
        AssertInvoiceQueriesDoNotReadXml(api);
    }

    [Fact]
    public async Task QueryWithoutInvoicesReturnsTwoEmptyArrays()
    {
        await using var api = await InvoiceApi.CreateAsync();
        using var response = await api.GetAsync(api.UserA, api.TenantA.ToString(), queryEndpoint: true);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Empty(json.RootElement.GetProperty("items").EnumerateArray());
        Assert.Empty(json.RootElement.GetProperty("summaries").EnumerateArray());
        Assert.Single(InvoiceQueries(api));
    }

    [Theory]
    [InlineData("anonymous", HttpStatusCode.Unauthorized)]
    [InlineData("foreign-tenant", HttpStatusCode.Forbidden)]
    [InlineData("unknown-user", HttpStatusCode.Forbidden)]
    [InlineData("missing-header", HttpStatusCode.BadRequest)]
    [InlineData("invalid-header", HttpStatusCode.BadRequest)]
    public async Task QueryUsesExistingAuthenticationAndTenantAuthorization(string mode, HttpStatusCode expected)
    {
        await using var api = await InvoiceApi.CreateAsync();
        string? user = mode == "anonymous" ? null : mode == "unknown-user" ? Guid.NewGuid().ToString() : api.UserA;
        string? tenant = mode switch { "foreign-tenant" => api.TenantB.ToString(), "missing-header" => null, "invalid-header" => "invalid", _ => api.TenantA.ToString() };
        using var response = await api.GetAsync(user, tenant, queryEndpoint: true);
        Assert.Equal(expected, response.StatusCode);
        Assert.Empty(InvoiceQueries(api));
    }

    [Theory]
    [InlineData("from=2026-11-14&to=2026-11-12&direction=Sales")]
    [InlineData("from=2026-11-12&to=2026-11-14&direction=999")]
    [InlineData("from=2026-11-12&to=2026-11-14&direction=Sales&dateKind=999")]
    [InlineData("from=2026-11-12&to=2026-11-14&direction=Sales&dateKind=InvoicingDate")]
    [InlineData("from=2026-11-12&to=2026-11-14&direction=Sales&dateKind=InvoicingDate&timeZoneId=Invalid/Zone")]
    [InlineData("from=2026-11-12&to=2026-11-14&direction=Sales&dateKind=invalid")]
    [InlineData("from=invalid&to=2026-11-14&direction=Sales")]
    [InlineData("from=2026-11-12&direction=Sales")]
    [InlineData("to=2026-11-14&direction=Sales")]
    [InlineData("from=2026-11-12&to=2026-11-14")]
    [InlineData("from=2026-03-08&to=2026-03-08&direction=Sales&dateKind=InvoicingDate&timeZoneId=America/Havana")]
    [InlineData("from=2026-11-01&to=2026-11-01&direction=Sales&dateKind=InvoicingDate&timeZoneId=America/Havana")]
    [InlineData("from=9999-12-31&to=9999-12-31&direction=Sales&dateKind=InvoicingDate&timeZoneId=UTC")]
    public async Task QueryRejectsInvalidFiltersBeforeReadingInvoices(string query)
    {
        await using var api = await InvoiceApi.CreateAsync();
        using var response = await api.GetAsync(api.UserA, api.TenantA.ToString(), query, queryEndpoint: true);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(InvoiceQueries(api));
    }

    private static string[] InvoiceQueries(InvoiceApi api) => api.Sql.Commands.Where(sql => sql.Contains("FROM [invoices]", StringComparison.Ordinal)).ToArray();

    private static async Task AddQueryInvoices(InvoiceApi api, params StoredInvoice[] invoices)
    {
        using var scope = api.Server.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Invoices.AddRange(invoices);
        await db.SaveChangesAsync();
    }

    private static void AssertInvoiceQueriesDoNotReadXml(InvoiceApi api)
    {
        var queries = api.Sql.Commands.Where(sql => sql.Contains("FROM [invoices]", StringComparison.Ordinal)).ToArray();
        Assert.NotEmpty(queries);
        Assert.All(queries, sql => Assert.DoesNotContain("[XmlBytes]", sql, StringComparison.Ordinal));
    }

    private sealed class ReadSqlInterceptor : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    private sealed class InvoiceApi : IAsyncDisposable
    {
        private readonly byte[] _signingKey = System.Security.Cryptography.RandomNumberGenerator.GetBytes(32);
        private readonly Guid _taxEntityId = Guid.NewGuid();
        private IHost _host = null!;
        public string Prefix { get; } = "invoice-read-test-" + Guid.NewGuid().ToString("N");
        public Guid TenantA { get; } = Guid.NewGuid();
        public Guid TenantB { get; } = Guid.NewGuid();
        public string UserA { get; } = Guid.NewGuid().ToString();
        public string UserB { get; } = Guid.NewGuid().ToString();
        public TestServer Server { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public ReadSqlInterceptor Sql { get; } = new();

        public static async Task<InvoiceApi> CreateAsync()
        {
            var api = new InvoiceApi();
            var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.Test.json", optional: false).Build();
            api._host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
                .ConfigureLogging(logging => logging.ClearProviders())
                .ConfigureServices(services =>
                {
                    services.AddSingleton<IConfiguration>(configuration);
                    services.AddDatabaseInfrastructure(configuration);
                    services.AddDbContext<AppDbContext>(options => options.AddInterceptors(api.Sql));
                    services.AddScoped<TenantAuthorizationFilter>();
                    // The real read method uses only the repository, without contacting KSeF or CredentialService.
                    services.AddScoped<IInvoiceService>(provider => new KsefInvoiceService(null!, null!, null!, null!, null!, null!, null!, provider.GetRequiredService<IInvoiceRepository>()));
                    services.AddControllers().AddApplicationPart(typeof(InvoicesController).Assembly);
                    services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options => options.TokenValidationParameters = new TokenValidationParameters
                    {
                        ValidateIssuer = true, ValidIssuer = "invoice-read-tests", ValidateAudience = true, ValidAudience = "invoice-read-tests",
                        ValidateLifetime = true, RequireExpirationTime = true, ValidateIssuerSigningKey = true,
                        IssuerSigningKey = new SymmetricSecurityKey(api._signingKey), RequireSignedTokens = true,
                        ValidAlgorithms = [SecurityAlgorithms.HmacSha256], ClockSkew = TimeSpan.Zero
                    });
                    services.AddAuthorization();
                }).Configure(app =>
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
                foreach (var user in new[] { (api.UserA, "a"), (api.UserB, "b") })
                {
                    string login = api.Prefix + user.Item2;
                    var result = await users.CreateAsync(new ApplicationUser { Id = user.Item1, UserName = login, Email = login + "@example.com" });
                    Assert.True(result.Succeeded, string.Join(", ", result.Errors.Select(x => x.Code)));
                }
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                var taxEntity = new TaxEntity { Id = api._taxEntityId, Nip = Random.Shared.NextInt64(1_000_000_000L, 10_000_000_000L).ToString(System.Globalization.CultureInfo.InvariantCulture), Name = api.Prefix };
                db.Tenants.AddRange(new Tenant { Id = api.TenantA, Name = api.Prefix + "a", TaxEntity = taxEntity }, new Tenant { Id = api.TenantB, Name = api.Prefix + "b", TaxEntity = taxEntity });
                db.UserTenants.AddRange(new UserTenant { TenantId = api.TenantA, UserId = api.UserA, Role = "User" }, new UserTenant { TenantId = api.TenantB, UserId = api.UserB, Role = "User" });
                await db.SaveChangesAsync();
                return api;
            }
            catch
            {
                await api.DisposeAsync();
                throw;
            }
        }

        public async Task<HttpResponseMessage> GetAsync(string? userId, string? tenantId, string query = "from=2026-11-01&to=2026-11-30&direction=Sales", bool queryEndpoint = false)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, (queryEndpoint ? "/api/invoices/query?" : "/api/invoices?") + query);
            if (userId is not null)
            {
                var token = new JwtSecurityToken(issuer: "invoice-read-tests", audience: "invoice-read-tests", claims: [new Claim(JwtRegisteredClaimNames.Sub, userId)], expires: DateTime.UtcNow.AddMinutes(5), signingCredentials: new SigningCredentials(new SymmetricSecurityKey(_signingKey), SecurityAlgorithms.HmacSha256));
                request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", new JwtSecurityTokenHandler().WriteToken(token));
            }
            if (tenantId is not null) request.Headers.Add("X-Tenant-Id", tenantId);
            return await Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                using var scope = Server.Services.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                Guid[] tenantIds = [TenantA, TenantB];
                string[] userIds = [UserA, UserB];
                await using var transaction = await db.Database.BeginTransactionAsync();
                await db.Invoices.Where(x => tenantIds.Contains(x.TenantId)).ExecuteDeleteAsync();
                await db.UserTenants.Where(x => tenantIds.Contains(x.TenantId)).ExecuteDeleteAsync();
                await db.Tenants.Where(x => tenantIds.Contains(x.Id)).ExecuteDeleteAsync();
                await db.TaxEntities.Where(x => x.Id == _taxEntityId).ExecuteDeleteAsync();
                await db.Users.Where(x => userIds.Contains(x.Id)).ExecuteDeleteAsync();
                await transaction.CommitAsync();
                Assert.False(await db.Invoices.AnyAsync(x => tenantIds.Contains(x.TenantId)));
                Assert.False(await db.Tenants.AnyAsync(x => tenantIds.Contains(x.Id)));
                Assert.False(await db.TaxEntities.AnyAsync(x => x.Id == _taxEntityId));
                Assert.False(await db.Users.AnyAsync(x => userIds.Contains(x.Id)));
            }
            finally
            {
                Client.Dispose();
                _host.Dispose();
            }
        }
    }
}
