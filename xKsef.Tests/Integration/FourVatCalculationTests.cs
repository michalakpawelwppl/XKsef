using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Application.SalesInvoices.Snapshots;
using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    [Theory]
    [InlineData(VatCalculationMethod.PerLineNet, "0.03")]
    [InlineData(VatCalculationMethod.PerLineGross, "0.03")]
    [InlineData(VatCalculationMethod.ByNetTotals, "0.02")]
    [InlineData(VatCalculationMethod.ByGrossTotals, "0.02")]
    public async Task FourVatMethodsPersistRecalculateApproveAndReserveWithoutChangingSnapshotFormat(VatCalculationMethod method, string expectedVat)
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var source = IssuanceDraft() with { NumberSeriesId = series.Id, VatMethod = (int)method,
            Lines = Enumerable.Range(0, 3).Select(_ => new SalesInvoiceLineDto { Description = "Towar", Unit = "szt.", UnitPrice = new(.03m), Quantity = new(1), PriceBasis = 0 }).ToArray() };
        var created = await CreateSalesAsync(browser, tenant, source);
        Assert.Equal(decimal.Parse(expectedVat, System.Globalization.CultureInfo.InvariantCulture), created.Totals.Vat!.Value.Value);
        Assert.Equal("sales-v3", created.CalculationVersion);
        using var recalc = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/recalculate", new RecalculateSalesInvoiceRequest(created.Version), tenantId: tenant);
        Assert.True(recalc.IsSuccessStatusCode, await recalc.Content.ReadAsStringAsync());
        var recalculated = (await recalc.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(created.Totals, recalculated.Totals);
        using var get = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        var saved = (await get.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal((int)method, saved.Draft.VatMethod); Assert.Equal(recalculated.Version, saved.Version);
        Assert.All(saved.LineResults, x => Assert.Equal(method is VatCalculationMethod.ByNetTotals or VatCalculationMethod.ByGrossTotals ? (int)VatAmountOrigin.TaxGroupAllocation : (int)VatAmountOrigin.PerLineCalculation, x.VatAmountOrigin));
        Assert.Equal((int)method, Assert.Single(saved.TaxSummaries).CalculationDetails!.Method);
        using var approval = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/approve", new ApproveSalesInvoiceRequest(saved.Version, saved.Revision), tenantId: tenant);
        Assert.True(approval.IsSuccessStatusCode, await approval.Content.ReadAsStringAsync());
        var approved = (await approval.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Set<SalesInvoiceApproval>().AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.SalesInvoiceId == created.Id);
            var snapshot = scope.ServiceProvider.GetRequiredService<ISalesInvoiceApprovalSnapshotReader>().Read(row);
            Assert.Equal("sales-v3", snapshot.CalculatorVersion); Assert.Equal((int)method, snapshot.Input!.VatMethod);
            Assert.Equal(saved.Totals.Vat!.Value.Value, snapshot.Calculations!.Totals!.Vat!.Value.Value);
            using var json = JsonDocument.Parse(row.SnapshotJson);
            Assert.True(json.RootElement.GetProperty("Input").TryGetProperty("Fa3", out _));
        }
        var reserved = await ReserveNumberAsync(f, tenant, NumberCommand(approved));
        var replay = await ReserveNumberAsync(f, tenant, NumberCommand(approved, reserved.OperationId));
        Assert.Equal(reserved.Id, replay.Id);
        using var edit = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(approved.Version, approved.Draft with { VatMethod = ((int)method + 1) % 4 }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
    }

    [Fact]
    public async Task SwitchingVatMethodChangesMoneyAndVersionWhilePreservingLineInputs()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var source = SalesDraft() with { VatMethod = 0, Lines = Enumerable.Range(0, 3).Select(_ => new SalesInvoiceLineDto { Description = "Towar", Unit = "szt.", UnitPrice = new(.03m), Quantity = new(1) }).ToArray() };
        var created = await CreateSalesAsync(browser, tenant, source);
        using var put = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(created.Version, created.Draft with { VatMethod = 2 }), tenantId: tenant);
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        var changed = (await put.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(.03m, created.Totals.Vat!.Value.Value); Assert.Equal(.02m, changed.Totals.Vat!.Value.Value);
        Assert.Equal(created.Revision + 1, changed.Revision); Assert.NotEqual(created.Version, changed.Version);
        Assert.Equal(created.Draft.Lines.Select(x => x.Id), changed.Draft.Lines.Select(x => x.Id));
        Assert.All(changed.Draft.Lines, x => { Assert.Equal(.03m, x.UnitPrice!.Value.Value); Assert.Equal(0, x.PriceBasis); });
    }

    [Theory]
    [InlineData(VatCalculationMethod.ByNetTotals, PriceBasis.Net, "0.008", "0.02", "0.00", "0.02")]
    [InlineData(VatCalculationMethod.ByGrossTotals, PriceBasis.Gross, "0.009", "0.02", "0.01", "0.03")]
    public async Task RoundedGroupBaseAndAllocatedAmountsSurviveRecalculationAndRead(VatCalculationMethod method, PriceBasis basis, string thirdPrice, string net, string vat, string gross)
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var prices = new[] { .008m, .008m, decimal.Parse(thirdPrice, System.Globalization.CultureInfo.InvariantCulture) };
        var source = SalesDraft() with { VatMethod = (int)method, Lines = prices.Select(price => new SalesInvoiceLineDto
            { Description = "Towar", Unit = "szt.", UnitPrice = new(price), Quantity = new(1), PriceBasis = (int)basis }).ToArray() };
        var created = await CreateSalesAsync(browser, tenant, source);
        using var recalc = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/recalculate", new RecalculateSalesInvoiceRequest(created.Version), tenantId: tenant);
        Assert.True(recalc.IsSuccessStatusCode, await recalc.Content.ReadAsStringAsync());
        using var get = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        Assert.True(get.IsSuccessStatusCode, await get.Content.ReadAsStringAsync());
        var saved = (await get.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(decimal.Parse(net, System.Globalization.CultureInfo.InvariantCulture), saved.Totals.Net!.Value.Value);
        Assert.Equal(decimal.Parse(vat, System.Globalization.CultureInfo.InvariantCulture), saved.Totals.Vat!.Value.Value);
        Assert.Equal(decimal.Parse(gross, System.Globalization.CultureInfo.InvariantCulture), saved.Totals.Gross!.Value.Value);
        Assert.Equal(created.Totals, saved.Totals); Assert.Equal(created.LineResults, saved.LineResults); Assert.Equal(created.TaxSummaries, saved.TaxSummaries);
        var summary = Assert.Single(saved.TaxSummaries);
        Assert.Equal((basis == PriceBasis.Net ? saved.Totals.Net : saved.Totals.Gross)!.Value.Value, summary.CalculationDetails!.TaxBase.Value);
        Assert.Equal(saved.Totals.Net.Value.Value, saved.LineResults.Sum(x => x.Net!.Value.Value));
        Assert.Equal(saved.Totals.Vat.Value.Value, saved.LineResults.Sum(x => x.Vat!.Value.Value));
        Assert.Equal(saved.Totals.Gross.Value.Value, saved.LineResults.Sum(x => x.Gross!.Value.Value));
        Assert.Equal(prices, saved.Draft.Lines.Select(x => x.UnitPrice!.Value.Value)); Assert.Equal(0, saved.Status);
        Assert.Null(saved.Approval); Assert.Null(saved.InvoiceNumber); Assert.Null(saved.KsefNumber);
    }
}
