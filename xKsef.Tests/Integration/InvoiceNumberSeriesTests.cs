using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Contracts.InvoiceNumberSeries;
using xKsef.Application.InvoiceNumbering;
using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.InvoiceNumberSeries;
using xKsef.Infrastructure.Persistence;
using xKsef.Infrastructure.Persistence.Repositories;
using NumberSeries = xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    private const string SeriesPath = "/api/invoice-number-series";
    private static NumberSeriesConfigurationDto SeriesConfiguration(string prefix = "FV", string? name = null) => new(name ?? prefix, prefix, "{prefix}/{number}/{year}", 6, 1);
    private static async Task<(Guid Tenant, Browser Browser)> SeriesScenarioAsync(Fixture f)
    {
        var tenant = await f.CreateTenantAsync();
        using (var scope = f.Scope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserTenants.Where(x => x.TenantId == tenant && x.UserId == f.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, "Admin"));
        var browser = f.Browser(); using var login = await browser.LoginAsync(); await browser.CsrfAsync(); return (tenant, browser);
    }
    private static async Task<InvoiceNumberSeriesDto> SeriesResponseAsync(Browser browser, Guid tenant, HttpMethod method, string path, object? body = null, HttpStatusCode status = HttpStatusCode.OK)
    {
        using var response = await browser.SendAsync(method, path, body, tenantId: tenant);
        Assert.True(response.StatusCode == status, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<InvoiceNumberSeriesDto>())!;
    }
    private static Task<InvoiceNumberSeriesDto> CreateSeriesAsync(Browser browser, Guid tenant, NumberSeriesConfigurationDto? configuration = null)
        => SeriesResponseAsync(browser, tenant, HttpMethod.Post, SeriesPath, configuration ?? SeriesConfiguration(), HttpStatusCode.Created);

    [Fact]
    public async Task NumberSeriesConfigurationRoundTripsAndHistoryPreservesOldValuesWithoutChangingInvoices()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser) = await SeriesScenarioAsync(f);
        var invoice = await CreateSalesAsync(browser, tenant, new());
        var created = await CreateSeriesAsync(browser, tenant);
        Assert.Equal(1, created.Configuration.StartingNumber); Assert.True(created.IsActive); Assert.Equal(1, created.Revision);
        Assert.Equal(f.UserId, created.CreatedBy); Assert.Single(created.History); Assert.Empty(created.Defaults);
        Assert.Equal(8, Convert.FromBase64String(created.Version).Length);
        var read = await SeriesResponseAsync(browser, tenant, HttpMethod.Get, $"{SeriesPath}/{created.Id}");
        Assert.Equal(JsonSerializer.Serialize(created), JsonSerializer.Serialize(read));
        f.Clock.Advance(TimeSpan.FromMinutes(1));
        var configuration = created.Configuration with { Name = "Nowa nazwa", Format = "{number}/{prefix}", ResetMode = 0, MinimumDigits = 4, StartingNumber = 10 };
        var updated = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{created.Id}", new UpdateInvoiceNumberSeriesRequest(created.Version, configuration));
        Assert.Equal(configuration, updated.Configuration); Assert.Equal(2, updated.Revision); Assert.NotEqual(created.Version, updated.Version);
        Assert.Equal(2, updated.History.Count); Assert.Equal(created.Configuration, updated.History[0].Configuration);
        Assert.Equal(f.Clock.GetUtcNow(), updated.UpdatedAt); Assert.Equal(f.UserId, updated.History[1].ChangedBy);
        using var list = await browser.SendAsync(HttpMethod.Get, SeriesPath, tenantId: tenant);
        Assert.Equal(updated.Id, Assert.Single((await list.Content.ReadFromJsonAsync<InvoiceNumberSeriesDto[]>())!).Id);
        using var invoiceRead = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{invoice.Id}", tenantId: tenant);
        var unchanged = (await invoiceRead.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(invoice.Version, unchanged.Version); Assert.Equal(invoice.Status, unchanged.Status);
        using var scope = f.Scope(); Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().Invoices.AnyAsync(x => x.TenantId == tenant));
    }

    [Fact]
    public async Task NumberSeriesDefaultsShareOneSeriesAcrossKindsAndDeactivationClearsAssignmentsAtomically()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser) = await SeriesScenarioAsync(f);
        var fv = await CreateSeriesAsync(browser, tenant); var fk = await CreateSeriesAsync(browser, tenant, SeriesConfiguration("FK"));
        foreach (var kind in new[] { 0, 1, 2 }) fv = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{fv.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(fv.Version, kind));
        fk = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{fk.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(fk.Version, 3));
        Assert.Equal(3, fv.Defaults.Count); Assert.Single(fk.Defaults);
        var oldDefault = fv.Defaults.Single(x => x.InvoiceKind == 0);
        fk = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{fk.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(fk.Version, 0, oldDefault.Version));
        Assert.Equal(2, fk.Defaults.Count);
        fv = await SeriesResponseAsync(browser, tenant, HttpMethod.Get, $"{SeriesPath}/{fv.Id}");
        Assert.DoesNotContain(fv.Defaults, x => x.InvoiceKind == 0); Assert.Equal((int)NumberSeriesChangeKind.DefaultRemoved, fv.History.Last().Change);
        fk = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{fk.Id}/activation", new SetInvoiceNumberSeriesActiveRequest(fk.Version, false));
        Assert.False(fk.IsActive); Assert.Empty(fk.Defaults);
        using var invalid = await browser.SendAsync(HttpMethod.Put, $"{SeriesPath}/{fk.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(fk.Version, 0), tenantId: tenant);
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Set<InvoiceNumberSeriesDefault>().AnyAsync(x => x.TenantId == tenant && x.SeriesId == fk.Id));
            Assert.Equal(2, await db.Set<InvoiceNumberSeriesDefault>().CountAsync(x => x.TenantId == tenant));
        }
        fk = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{fk.Id}/activation", new SetInvoiceNumberSeriesActiveRequest(fk.Version, true));
        Assert.True(fk.IsActive); Assert.Empty(fk.Defaults);
        fk = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{fk.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(fk.Version, 3));
        var same = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{fk.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(fk.Version, 3, fk.Defaults.Single().Version));
        Assert.Equal(fk.Revision, same.Revision);
    }

    [Fact]
    public async Task NumberSeriesCollisionChecksIncludeInactiveHistoryLiteralFormatsAndDifferentPadding()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser) = await SeriesScenarioAsync(f);
        var series = await CreateSeriesAsync(browser, tenant);
        foreach (var configuration in new[] { SeriesConfiguration("FV", "Inna nazwa") with { MinimumDigits = 1 }, SeriesConfiguration("OTHER", "Literal") with { Format = "FV/{number}/{year}" }, SeriesConfiguration("FK", "fv") })
        {
            using var collision = await browser.SendAsync(HttpMethod.Post, SeriesPath, configuration, tenantId: tenant); Assert.Equal(HttpStatusCode.Conflict, collision.StatusCode);
        }
        series = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{series.Id}", new UpdateInvoiceNumberSeriesRequest(series.Version, series.Configuration with { Prefix = "FD" }));
        series = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{series.Id}/activation", new SetInvoiceNumberSeriesActiveRequest(series.Version, false));
        using var historyCollision = await browser.SendAsync(HttpMethod.Post, SeriesPath, SeriesConfiguration("FV", "Ponownie FV"), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Conflict, historyCollision.StatusCode); Assert.Contains("FormatCollision", await historyCollision.Content.ReadAsStringAsync());
        using var scope = f.Scope(); Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<NumberSeries>().CountAsync(x => x.TenantId == tenant));
    }

    [Fact]
    public async Task NumberSeriesTenantIsolationAndBackendPermissionsProtectEveryOperation()
    {
        await using var f = await Fixture.CreateAsync(); var (a, browser) = await SeriesScenarioAsync(f); var (b, _) = await SeriesScenarioAsync(f);
        var foreign = await f.CreateTenantAsync(f.OtherId); var series = await CreateSeriesAsync(browser, a); var other = await CreateSeriesAsync(browser, b);
        using var read = await browser.SendAsync(HttpMethod.Get, $"{SeriesPath}/{series.Id}", tenantId: b); Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        using var update = await browser.SendAsync(HttpMethod.Put, $"{SeriesPath}/{series.Id}", new UpdateInvoiceNumberSeriesRequest(series.Version, series.Configuration), tenantId: b); Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        using var defaultForeign = await browser.SendAsync(HttpMethod.Put, $"{SeriesPath}/{other.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(other.Version, 0), tenantId: a); Assert.Equal(HttpStatusCode.NotFound, defaultForeign.StatusCode);
        using var denied = await browser.SendAsync(HttpMethod.Get, SeriesPath, tenantId: foreign); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var list = await browser.SendAsync(HttpMethod.Get, SeriesPath, tenantId: a); Assert.Equal(series.Id, Assert.Single((await list.Content.ReadFromJsonAsync<InvoiceNumberSeriesDto[]>())!).Id);
        using (var scope = f.Scope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserTenants.Where(x => x.TenantId == a && x.UserId == f.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, "User"));
        foreach (var operation in new[] { HttpMethod.Get, HttpMethod.Post })
        {
            using var permission = await browser.SendAsync(operation, SeriesPath, operation == HttpMethod.Post ? SeriesConfiguration("FK") : null, tenantId: a);
            Assert.Equal(HttpStatusCode.Forbidden, permission.StatusCode); Assert.Contains("InvoiceNumberSeries.Manage", await permission.Content.ReadAsStringAsync());
        }
        using var anonymous = await f.Browser().SendAsync(HttpMethod.Get, SeriesPath, tenantId: a); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        browser.Token = null;
        using var csrf = await browser.SendAsync(HttpMethod.Post, SeriesPath, SeriesConfiguration("FK"), tenantId: a); Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
    }

    [Fact]
    public async Task NumberSeriesStaleConfigurationAndDefaultVersionsCannotOverwriteChanges()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser) = await SeriesScenarioAsync(f);
        var first = await CreateSeriesAsync(browser, tenant); var second = await CreateSeriesAsync(browser, tenant, SeriesConfiguration("FK"));
        var assigned = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{first.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(first.Version, 0));
        using var stale = await browser.SendAsync(HttpMethod.Put, $"{SeriesPath}/{first.Id}", new UpdateInvoiceNumberSeriesRequest(first.Version, first.Configuration with { Name = "Stare dane" }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var missingExpected = await browser.SendAsync(HttpMethod.Put, $"{SeriesPath}/{second.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(second.Version, 0), tenantId: tenant); Assert.Equal(HttpStatusCode.Conflict, missingExpected.StatusCode);
        second = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{second.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(second.Version, 0, assigned.Defaults.Single().Version));
        first = await SeriesResponseAsync(browser, tenant, HttpMethod.Get, $"{SeriesPath}/{first.Id}");
        using var oldDefaultVersion = await browser.SendAsync(HttpMethod.Put, $"{SeriesPath}/{first.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(first.Version, 0, assigned.Defaults.Single().Version), tenantId: tenant); Assert.Equal(HttpStatusCode.Conflict, oldDefaultVersion.StatusCode);
        using var malformed = await browser.SendAsync(HttpMethod.Put, $"{SeriesPath}/{first.Id}", new UpdateInvoiceNumberSeriesRequest("bad", first.Configuration), tenantId: tenant); Assert.Equal(HttpStatusCode.BadRequest, malformed.StatusCode);
    }

    [Theory]
    [InlineData("create")]
    [InlineData("edit")]
    [InlineData("default")]
    public async Task NumberSeriesConcurrentChangesHaveOneWinnerAndNoLostHistory(string operation)
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, firstBrowser) = await SeriesScenarioAsync(f);
        var secondBrowser = f.Browser(); using var login = await secondBrowser.LoginAsync(); await secondBrowser.CsrfAsync();
        var one = operation == "create" ? null : await CreateSeriesAsync(firstBrowser, tenant);
        var two = operation == "default" ? await CreateSeriesAsync(firstBrowser, tenant, SeriesConfiguration("FK")) : one;
        Task<HttpResponseMessage> Send(Browser browser, InvoiceNumberSeriesDto? series, string name) => operation switch
        {
            "create" => browser.SendAsync(HttpMethod.Post, SeriesPath, SeriesConfiguration("FV", name), tenantId: tenant),
            "edit" => browser.SendAsync(HttpMethod.Put, $"{SeriesPath}/{series!.Id}", new UpdateInvoiceNumberSeriesRequest(series.Version, series.Configuration with { Name = name }), tenantId: tenant),
            _ => browser.SendAsync(HttpMethod.Put, $"{SeriesPath}/{series!.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(series.Version, 0), tenantId: tenant)
        };
        var results = await Task.WhenAll(Send(firstBrowser, one, "Pierwsza"), Send(secondBrowser, two, "Druga"));
        try
        {
            Assert.Equal(1, results.Count(x => x.StatusCode == HttpStatusCode.Conflict));
            Assert.Equal(1, results.Count(x => x.StatusCode == (operation == "create" ? HttpStatusCode.Created : HttpStatusCode.OK)));
        }
        finally { foreach (var result in results) result.Dispose(); }
        using var check = f.Scope(); var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(operation == "create" ? 1 : operation == "edit" ? 2 : 3, await db.Set<InvoiceNumberSeriesRevision>().CountAsync(x => x.TenantId == tenant));
        if (operation == "default") Assert.Single(await db.Set<InvoiceNumberSeriesDefault>().Where(x => x.TenantId == tenant).ToArrayAsync());
    }

    [Fact]
    public async Task NumberSeriesDeactivationFailureRollsBackConfigurationHistoryAndDefaultsTogether()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser) = await SeriesScenarioAsync(f);
        var series = await CreateSeriesAsync(browser, tenant);
        series = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{series.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(series.Version, 0));
        using var scope = f.Scope(); var provider = scope.ServiceProvider;
        var connection = provider.GetRequiredService<AppDbContext>().Database.GetConnectionString();
        await using var failing = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).AddInterceptors(new NumberSeriesSaveFailure()).Options);
        var service = ActivatorUtilities.CreateInstance<InvoiceNumberSeriesService>(provider, new InvoiceNumberSeriesRepository(failing));
        await Assert.ThrowsAsync<IOException>(() => service.SetActiveAsync(tenant, series.Id, f.UserId, new(series.Version, false)));
        var actual = await SeriesResponseAsync(browser, tenant, HttpMethod.Get, $"{SeriesPath}/{series.Id}");
        Assert.Equal(JsonSerializer.Serialize(series), JsonSerializer.Serialize(actual));
    }

    [Fact]
    public async Task NumberSeriesSqlConstraintsRejectForeignDefaultsDuplicateAssignmentsAndInvalidConfiguration()
    {
        await using var f = await Fixture.CreateAsync(); var (a, browser) = await SeriesScenarioAsync(f); var (b, _) = await SeriesScenarioAsync(f);
        var seriesA = await CreateSeriesAsync(browser, a); var seriesB = await CreateSeriesAsync(browser, b);
        seriesA = await SeriesResponseAsync(browser, a, HttpMethod.Put, $"{SeriesPath}/{seriesA.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(seriesA.Version, 0));
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO InvoiceNumberSeriesDefaults (TenantId,InvoiceKind,SeriesId) VALUES ({a},{1},{seriesB.Id})"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO InvoiceNumberSeriesDefaults (TenantId,InvoiceKind,SeriesId) VALUES ({a},{0},{seriesA.Id})"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE InvoiceNumberSeries SET StartingNumber={0} WHERE TenantId={a} AND Id={seriesA.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE InvoiceNumberSeriesHistory SET Configuration={"invalid JSON"} WHERE TenantId={a} AND SeriesId={seriesA.Id}"));
        Assert.Equal(1, await db.Set<InvoiceNumberSeriesDefault>().CountAsync(x => x.TenantId == a));
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }

    [Theory]
    [InlineData(0, 1, 1, "{prefix}/{number}/{year}")]
    [InlineData(20, 1, 1, "{prefix}/{number}/{year}")]
    [InlineData(6, 99, 1, "{prefix}/{number}/{year}")]
    [InlineData(6, 1, 0, "{prefix}/{number}/{year}")]
    [InlineData(6, 1, 1, "{prefix}/{number}")]
    [InlineData(6, 0, 1, "{prefix}/{unknown}")]
    public async Task NumberSeriesInvalidRequestsDoNotLeaveConfigurationOrHistory(int digits, int reset, long start, string format)
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser) = await SeriesScenarioAsync(f);
        using var response = await browser.SendAsync(HttpMethod.Post, SeriesPath, new NumberSeriesConfigurationDto("FV", "FV", format, digits, reset, start), tenantId: tenant);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Set<NumberSeries>().AnyAsync(x => x.TenantId == tenant)); Assert.False(await db.Set<InvoiceNumberSeriesRevision>().AnyAsync(x => x.TenantId == tenant));
    }

    private sealed class NumberSeriesSaveFailure : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
            => throw new IOException("Symulowana awaria po zapisie SQL, przed commitem konfiguracji.");
    }
}
