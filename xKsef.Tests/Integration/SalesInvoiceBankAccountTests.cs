using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Application.SalesInvoices.Snapshots;
using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.SalesInvoices;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    [Fact]
    public async Task SalesInvoiceBankAccountsRoundTripEditApproveAndReserveUsingTheExactSnapshot()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var seller = new BankAccountDto("seller/123", "WBKPPLPP", "Bank sprzedawcy", "Sprzedawca", 1);
        var factors = new[] { new BankAccountDto("factor/123", "ABCDEFGH123", "Bank faktora", "Pierwszy", 2), new BankAccountDto("factor/456", BankOwnAccountType: 3) };
        var created = await CreateSalesAsync(browser, tenant, IssuanceDraft() with
        { NumberSeriesId = series.Id, Details = new() { BankAccounts = [seller], FactorBankAccounts = factors } });
        Assert.Equal(seller, Assert.Single(created.Draft.Details.BankAccounts));
        Assert.Equal(factors, created.Draft.Details.FactorBankAccounts);
        await f.RestartAsync();
        using var get = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        var stored = (await get.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(seller, Assert.Single(stored.Draft.Details.BankAccounts)); Assert.Equal(factors, stored.Draft.Details.FactorBankAccounts);

        var changed = stored.Draft with { Details = stored.Draft.Details with { BankAccounts = [seller with { BankOwnAccountType = 3 }], FactorBankAccounts = factors.Reverse().ToArray() } };
        using var update = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(stored.Version, changed), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode); var current = (await update.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(stored.Revision + 1, current.Revision); Assert.NotEqual(stored.Version, current.Version); Assert.Equal(stored.Totals, current.Totals);
        using var stale = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(stored.Version, stored.Draft), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var confirm = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(current.Version,
            current.Draft with { Fa3 = current.Draft.Fa3 with { Qualifications = SalesInvoiceSnapshotTestData.ConfirmedQualificationsDto() } }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode); current = (await confirm.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        using var approval = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/approve", new ApproveSalesInvoiceRequest(current.Version, current.Revision), tenantId: tenant);
        Assert.True(approval.IsSuccessStatusCode, await approval.Content.ReadAsStringAsync()); var approved = (await approval.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        string json;
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Set<SalesInvoiceApproval>().AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.SalesInvoiceId == created.Id);
            json = row.SnapshotJson;
            var snapshot = scope.ServiceProvider.GetRequiredService<ISalesInvoiceApprovalSnapshotReader>().Read(row);
            Assert.Equal(3, Assert.Single(snapshot.Input!.Details!.BankAccounts!).BankOwnAccountType);
            Assert.Equal(new[] { "factor/456", "factor/123" }, snapshot.Input.Details.FactorBankAccounts!.Select(x => x.Number));
            Assert.Equal(new int?[] { 3, 2 }, snapshot.Input.Details.FactorBankAccounts!.Select(x => x.BankOwnAccountType));
        }
        using var forbiddenEdit = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(approved.Version, created.Draft), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Conflict, forbiddenEdit.StatusCode);
        var reserved = await ReserveNumberAsync(f, tenant, NumberCommand(approved));
        Assert.Equal(approved.Approval!.Id, reserved.ApprovalId);
        using (var scope = f.Scope())
        {
            var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<SalesInvoiceApproval>().AsNoTracking().SingleAsync(x => x.Id == approved.Approval.Id);
            Assert.Equal(json, row.SnapshotJson);
        }
    }

    [Fact]
    public async Task SalesInvoiceBankAccountsEnforceTenantBoundaryAndAllowFactorOnlyDrafts()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var other = await f.CreateTenantAsync(); var foreign = await f.CreateTenantAsync(f.OtherId);
        var browser = f.Browser(); using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, tenant, new() { Details = new() { FactorBankAccounts = [new("factor/123", BankOwnAccountType: 2)] } });
        Assert.Empty(created.Draft.Details.BankAccounts); Assert.Equal(2, Assert.Single(created.Draft.Details.FactorBankAccounts).BankOwnAccountType);
        var path = $"/api/sales-invoices/{created.Id}";
        using var wrongRead = await browser.SendAsync(HttpMethod.Get, path, tenantId: other); Assert.Equal(HttpStatusCode.NotFound, wrongRead.StatusCode);
        using var wrongEdit = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(created.Version, created.Draft), tenantId: other); Assert.Equal(HttpStatusCode.NotFound, wrongEdit.StatusCode);
        using var denied = await browser.SendAsync(HttpMethod.Get, path, tenantId: foreign); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var anonymous = await f.Browser().SendAsync(HttpMethod.Get, path, tenantId: tenant); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var clear = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(created.Version, created.Draft with { Details = new() }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, clear.StatusCode); var empty = (await clear.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Empty(empty.Draft.Details.BankAccounts); Assert.Empty(empty.Draft.Details.FactorBankAccounts);
    }

    [Theory]
    [InlineData("type")] [InlineData("swift")] [InlineData("number")] [InlineData("count")] [InlineData("nullCollection")] [InlineData("nullEntry")]
    public async Task SalesInvoiceBankAccountsRejectInvalidDraftUpdatesWithoutChangingPersistedData(string problem)
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, tenant); var path = $"/api/sales-invoices/{created.Id}";
        IReadOnlyList<BankAccountDto> factors = problem switch
        {
            "type" => [new("1234567890", BankOwnAccountType: 0)], "swift" => [new("1234567890", "invalid")],
            "number" => [new("short")], "count" => Enumerable.Repeat(new BankAccountDto("1234567890"), 21).ToArray(),
            "nullCollection" => null!, _ => [null!]
        };
        using var response = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(created.Version,
            created.Draft with { Details = created.Draft.Details with { FactorBankAccounts = factors } }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var get = await browser.SendAsync(HttpMethod.Get, path, tenantId: tenant); var unchanged = (await get.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(created.Revision, unchanged.Revision); Assert.Equal(created.Version, unchanged.Version);
        Assert.Equal(created.Draft.Details.BankAccounts, unchanged.Draft.Details.BankAccounts); Assert.Empty(unchanged.Draft.Details.FactorBankAccounts);
    }
}
