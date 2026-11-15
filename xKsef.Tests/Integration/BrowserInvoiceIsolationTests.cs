using System.Collections.Concurrent;
using System.Data.Common;
using System.Net;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Contracts.Invoices;
using xKsef.Domain.Invoices;
using xKsef.Infrastructure.Persistence;
using xKsef.Tests.Infrastructure;
using Direction = xKsef.Domain.Invoices.InvoiceDirection;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    [Theory]
    [InlineData("Sales", "IssueDate")]
    [InlineData("Purchase", "IssueDate")]
    [InlineData("Sales", "InvoicingDate")]
    [InlineData("Purchase", "InvoicingDate")]
    public async Task CookieInvoiceQueryReadsOnlyOwnTenantRowsAndSummaries(string direction, string dateKind)
    {
        await using var f = await Fixture.CreateAsync();
        var data = await SeedCookieInvoicesAsync(f);
        var one = f.Browser(); var two = f.Browser();
        using var loginOne = await one.LoginAsync(); using var loginTwo = await two.LoginAsync(f.OtherLogin);
        Assert.Equal(HttpStatusCode.OK, loginOne.StatusCode); Assert.Equal(HttpStatusCode.OK, loginTwo.StatusCode);

        await AssertCookieInvoiceQueryAsync(f, one, data.TenantA, direction, dateKind, data.Rows, f.Prefix + "-B");
        await AssertCookieInvoiceQueryAsync(f, two, data.TenantB, direction, dateKind, data.Rows, f.Prefix + "-A");
    }

    [Theory]
    [InlineData("Sales", "IssueDate")]
    [InlineData("Purchase", "IssueDate")]
    [InlineData("Sales", "InvoicingDate")]
    [InlineData("Purchase", "InvoicingDate")]
    public async Task CookieInvoiceQueryRejectsForeignTenantHeaderWithoutReadingInvoices(string direction, string dateKind)
    {
        await using var f = await Fixture.CreateAsync();
        var data = await SeedCookieInvoicesAsync(f);
        var one = f.Browser(); var two = f.Browser();
        using var loginOne = await one.LoginAsync(); using var loginTwo = await two.LoginAsync(f.OtherLogin);
        Assert.Equal(HttpStatusCode.OK, loginOne.StatusCode); Assert.Equal(HttpStatusCode.OK, loginTwo.StatusCode);

        foreach (var (browser, foreignTenant) in new[] { (one, data.TenantB), (two, data.TenantA) })
        {
            f.InvoiceSql.Commands.Clear();
            using var denied = await browser.SendAsync(HttpMethod.Get, CookieInvoiceQueryPath(direction, dateKind), tenantId: foreignTenant);
            await AssertCookieInvoiceDeniedAsync(f, denied, HttpStatusCode.Forbidden, data.Rows);
        }
        // A forbidden tenant header must not invalidate either authenticated browser session.
        await AssertCookieInvoiceQueryAsync(f, one, data.TenantA, direction, dateKind, data.Rows, f.Prefix + "-B");
        await AssertCookieInvoiceQueryAsync(f, two, data.TenantB, direction, dateKind, data.Rows, f.Prefix + "-A");
    }

    [Theory]
    [InlineData("Sales", "IssueDate")]
    [InlineData("Purchase", "IssueDate")]
    [InlineData("Sales", "InvoicingDate")]
    [InlineData("Purchase", "InvoicingDate")]
    public async Task CookieInvoiceQueryRejectsRevokedMembershipOnTheNextRequest(string direction, string dateKind)
    {
        await using var f = await Fixture.CreateAsync();
        var data = await SeedCookieInvoicesAsync(f);
        var one = f.Browser(); var two = f.Browser();
        using var loginOne = await one.LoginAsync(); using var loginTwo = await two.LoginAsync(f.OtherLogin);
        Assert.Equal(HttpStatusCode.OK, loginOne.StatusCode); Assert.Equal(HttpStatusCode.OK, loginTwo.StatusCode);
        await AssertCookieInvoiceQueryAsync(f, one, data.TenantA, direction, dateKind, data.Rows, f.Prefix + "-B");

        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(1, await db.UserTenants.Where(row => row.UserId == f.UserId && row.TenantId == data.TenantA).ExecuteDeleteAsync());
        }
        f.InvoiceSql.Commands.Clear();
        using var denied = await one.SendAsync(HttpMethod.Get, CookieInvoiceQueryPath(direction, dateKind), tenantId: data.TenantA);
        await AssertCookieInvoiceDeniedAsync(f, denied, HttpStatusCode.Forbidden, data.Rows);
        using var me = await one.SendAsync(HttpMethod.Get, "/api/browser-auth/me");
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        // Revoking user A's membership does not affect user B or delete either tenant's invoices.
        await AssertCookieInvoiceQueryAsync(f, two, data.TenantB, direction, dateKind, data.Rows, f.Prefix + "-A");
        using var verification = f.Scope();
        var verificationDb = verification.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(data.Rows.Length, await verificationDb.Invoices.CountAsync(row => row.TenantId == data.TenantA || row.TenantId == data.TenantB));
    }

    [Theory]
    [InlineData("Sales", "IssueDate")]
    [InlineData("Purchase", "IssueDate")]
    [InlineData("Sales", "InvoicingDate")]
    [InlineData("Purchase", "InvoicingDate")]
    public async Task CookieInvoiceQueryRejectsAnonymousRequestsWithoutReadingInvoices(string direction, string dateKind)
    {
        await using var f = await Fixture.CreateAsync();
        var data = await SeedCookieInvoicesAsync(f);
        f.InvoiceSql.Commands.Clear();
        using var denied = await f.Browser().SendAsync(HttpMethod.Get, CookieInvoiceQueryPath(direction, dateKind), tenantId: data.TenantA);
        await AssertCookieInvoiceDeniedAsync(f, denied, HttpStatusCode.Unauthorized, data.Rows);
    }

    private static string CookieInvoiceQueryPath(string direction, string dateKind) =>
        $"/api/invoices/query?from=2026-11-12&to=2026-11-12&direction={direction}&dateKind={dateKind}"
        + (dateKind == "InvoicingDate" ? "&timeZoneId=Europe%2FWarsaw" : "");

    private static async Task<(Guid TenantA, Guid TenantB, StoredInvoice[] Rows)> SeedCookieInvoicesAsync(Fixture f)
    {
        Guid tenantA = await f.CreateTenantAsync(); Guid tenantB = await f.CreateTenantAsync(f.OtherId);
        var rows = new List<StoredInvoice>();
        foreach (var (tenantId, owner, currency, net) in new[] { (tenantA, "A", "PLN", 100.12m), (tenantB, "B", "EUR", 9000.34m) })
        foreach (var direction in new[] { Direction.Sales, Direction.Purchase })
        foreach (var dateKind in new[] { "IssueDate", "InvoicingDate" })
        foreach (var identity in new[] { "shared", owner })
        {
            // Each group contains a KSeF number shared across tenants and a tenant-specific number.
            var row = InvoiceReadTestData.Create(tenantId, $"{f.Prefix}-{direction}-{dateKind}-{identity}");
            row.InvoiceNumber = $"{f.Prefix}-{owner}-{direction}-{dateKind}-{identity}";
            row.SellerName = f.Prefix + "-" + owner; row.BuyerName = f.Prefix + "-" + owner;
            row.Direction = direction; row.Currency = currency;
            row.NetAmount = net; row.VatAmount = 23.45m; row.GrossAmount = net + 23.45m;
            // Disjoint dates detect accidental fallback to IssueDate as well as ignoring the requested date kind.
            row.IssueDate = dateKind == "IssueDate" ? new DateOnly(2026, 11, 12) : new DateOnly(2026, 10, 1);
            row.InvoicingDate = dateKind == "InvoicingDate"
                ? new DateTimeOffset(2026, 11, 12, 0, 30, 0, TimeSpan.FromHours(1))
                : new DateTimeOffset(2026, 12, 1, 12, 0, 0, TimeSpan.Zero);
            rows.Add(row);
        }
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        db.Invoices.AddRange(rows); await db.SaveChangesAsync();
        return (tenantA, tenantB, rows.ToArray());
    }

    private static async Task AssertCookieInvoiceQueryAsync(Fixture f, Browser browser, Guid tenantId, string direction, string dateKind, StoredInvoice[] rows, string foreignMarker)
    {
        f.InvoiceSql.Commands.Clear();
        using var response = await browser.SendAsync(HttpMethod.Get, CookieInvoiceQueryPath(direction, dateKind), tenantId: tenantId);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Null(response.RequestMessage!.Headers.Authorization);
        Assert.Contains("__Host-xksef-session=", response.RequestMessage.Headers.GetValues("Cookie").Single());
        Assert.Equal("application/json", response.Content.Headers.ContentType!.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl!.ToString());
        string body = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain(foreignMarker, body);
        using var json = JsonDocument.Parse(body);
        var result = JsonSerializer.Deserialize<InvoiceQueryResultDto>(body, JsonSerializerOptions.Web)!;
        var expected = rows.Where(row => row.TenantId == tenantId && row.Direction.ToString() == direction
            && (dateKind == "IssueDate" ? row.IssueDate == new DateOnly(2026, 11, 12) : row.IssueDate == new DateOnly(2026, 10, 1)))
            .OrderByDescending(row => row.Id).ToArray();
        Assert.Equal(2, expected.Length);
        Assert.Equal(expected.Select(row => row.KsefNumber), result.Items.Select(row => row.KsefNumber));
        foreach (var item in json.RootElement.GetProperty("items").EnumerateArray())
            InvoiceReadTestData.AssertJson(item, expected.Single(row => row.KsefNumber == item.GetProperty("ksefNumber").GetString()));
        Assert.Equal(new InvoiceCurrencySummaryDto(expected[0].Currency, 2, expected.Sum(row => row.NetAmount!.Value),
            expected.Sum(row => row.VatAmount!.Value), expected.Sum(row => row.GrossAmount), 0, 0), Assert.Single(result.Summaries));
        foreach (var foreign in rows.Where(row => row.TenantId != tenantId)) Assert.DoesNotContain(foreign.InvoiceNumber, body);
        string sql = Assert.Single(f.InvoiceSql.Commands);
        Assert.Contains("[TenantId] =", sql); Assert.Contains("[Direction] =", sql);
        Assert.Contains(dateKind == "IssueDate" ? "[IssueDate] >=" : "[InvoicingDate] >=", sql);
        Assert.DoesNotContain("[XmlBytes]", sql);
    }

    private static async Task AssertCookieInvoiceDeniedAsync(Fixture f, HttpResponseMessage response, HttpStatusCode status, StoredInvoice[] rows)
    {
        Assert.Equal(status, response.StatusCode); Assert.Null(response.Headers.Location);
        Assert.Empty(f.InvoiceSql.Commands);
        string body = await response.Content.ReadAsStringAsync();
        foreach (var row in rows)
        {
            Assert.DoesNotContain(row.KsefNumber, body); Assert.DoesNotContain(row.InvoiceNumber, body);
            Assert.DoesNotContain(row.SellerName, body);
        }
        Assert.DoesNotContain("\"items\"", body); Assert.DoesNotContain("\"summaries\"", body);
    }

    private sealed class InvoiceReadSqlInterceptor : DbCommandInterceptor
    {
        public ConcurrentQueue<string> Commands { get; } = new();
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("FROM [invoices]", StringComparison.Ordinal)) Commands.Enqueue(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }

    // Required by the controller constructor, but /query must use IInvoiceQueryService from AddDatabaseInfrastructure.
    private sealed class UnexpectedInvoiceService : IInvoiceService
    {
        public Task<IReadOnlyList<Invoice>> GetInvoicesAsync(Guid tenantId, DateOnly from, DateOnly to, Direction direction, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Invoice query must use SQL, not the legacy invoice service.");
        public Task<string> GetInvoiceXmlAsync(string userId, Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default) => throw new InvalidOperationException("External invoice operations are forbidden.");
        public Task<InvoicePackageReadResult> ImportInvoicesAsync(string userId, Guid tenantId, DateOnly from, DateOnly to, Direction direction, CancellationToken cancellationToken = default) => throw new InvalidOperationException("KSeF calls are forbidden.");
    }
}
