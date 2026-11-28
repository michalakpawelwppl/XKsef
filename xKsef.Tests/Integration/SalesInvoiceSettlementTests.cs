using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Application.SalesInvoices.Snapshots;
using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.SalesInvoices;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    private static InvoiceSettlementDto SettlementInput(decimal? charge = 17m, decimal? deduction = 300m) => new()
    { Charges = [new(charge is null ? null : new(charge.Value), "Zwrot kosztów")], Deductions = [new(deduction is null ? null : new(deduction.Value), "Rozliczenie salda")] };

    [Fact]
    public async Task SalesInvoiceSettlementPersistsMfExampleEditsRecalculatesAndFreezesSnapshotBeforeNumbering()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var created = await CreateSalesAsync(browser, tenant, IssuanceDraft() with
        { NumberSeriesId = series.Id, Lines = [new() { Description = "Usługa", Unit = "szt.", Quantity = new(1), UnitPrice = new(200) }], Details = new() { Settlement = SettlementInput() } });
        Assert.Equal(246m, created.Totals.Gross!.Value.Value); Assert.Equal(46m, created.Totals.Vat!.Value.Value);
        Assert.Equal(17m, created.SettlementAmounts!.ChargesTotal!.Value.Value); Assert.Equal(300m, created.SettlementAmounts.DeductionsTotal!.Value.Value);
        Assert.Null(created.SettlementAmounts.AmountDue); Assert.Equal(37m, created.SettlementAmounts.Overpayment!.Value.Value);
        await f.RestartAsync(); var path = $"/api/sales-invoices/{created.Id}";
        using var read = await browser.SendAsync(HttpMethod.Get, path, tenantId: tenant); var stored = (await read.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(created.SettlementAmounts, stored.SettlementAmounts); Assert.Equal("Zwrot kosztów", stored.Draft.Details.Settlement!.Charges[0].Reason);
        using var edit = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(stored.Version, stored.Draft with { Details = stored.Draft.Details with { Settlement = SettlementInput(deduction: 10m) } }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode); var current = (await edit.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(stored.Totals, current.Totals); Assert.Equal(253m, current.SettlementAmounts!.AmountDue!.Value.Value); Assert.Null(current.SettlementAmounts.Overpayment);
        using var staleEdit = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(stored.Version, stored.Draft), tenantId: tenant); Assert.Equal(HttpStatusCode.Conflict, staleEdit.StatusCode);
        using var staleApproval = await browser.SendAsync(HttpMethod.Post, path + "/approve", new ApproveSalesInvoiceRequest(current.Version, stored.Revision), tenantId: tenant); Assert.Equal(HttpStatusCode.Conflict, staleApproval.StatusCode);
        using var confirm = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(current.Version, current.Draft with
        { Fa3 = current.Draft.Fa3 with { Qualifications = SalesInvoiceSnapshotTestData.ConfirmedQualificationsDto() } }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode); current = (await confirm.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        using var approval = await browser.SendAsync(HttpMethod.Post, path + "/approve", new ApproveSalesInvoiceRequest(current.Version, current.Revision), tenantId: tenant);
        Assert.True(approval.IsSuccessStatusCode, await approval.Content.ReadAsStringAsync()); var approved = (await approval.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        string json;
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Set<SalesInvoiceApproval>().AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.SalesInvoiceId == created.Id); json = row.SnapshotJson;
            var snapshot = scope.ServiceProvider.GetRequiredService<ISalesInvoiceApprovalSnapshotReader>().Read(row);
            Assert.Equal(17m, snapshot.Input!.Details!.Settlement!.Charges![0].Amount!.Value.Value);
            Assert.Equal(253m, snapshot.Calculations!.SettlementAmounts!.AmountDue!.Value.Value);
            Assert.Equal(246m, snapshot.Calculations.Totals!.Gross!.Value.Value);
            Assert.Equal(253m, (await db.SalesInvoices.AsNoTracking().SingleAsync(x => x.Id == created.Id)).SettlementAmounts!.AmountDue);
        }
        using var blocked = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(approved.Version, created.Draft), tenantId: tenant); Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var reservation = await ReserveNumberAsync(f, tenant, NumberCommand(approved)); Assert.Equal(approved.Approval!.Id, reservation.ApprovalId);
        using (var scope = f.Scope()) Assert.Equal(json, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<SalesInvoiceApproval>().SingleAsync(x => x.Id == approved.Approval.Id)).SnapshotJson);
    }

    [Fact]
    public async Task SalesInvoiceSettlementAllowsIncompleteDraftButRejectsApprovalAndCanBeRemoved()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var created = await CreateSalesAsync(browser, tenant, IssuanceDraft() with { NumberSeriesId = series.Id, Details = new() { Settlement = new() { Charges = [new()] } } });
        Assert.Null(created.SettlementAmounts!.ChargesTotal); Assert.Null(created.SettlementAmounts.AmountDue); Assert.Null(created.SettlementAmounts.Overpayment);
        var path = $"/api/sales-invoices/{created.Id}";
        using var check = await browser.SendAsync(HttpMethod.Post, path + "/validate-issuance", tenantId: tenant);
        var validation = (await check.Content.ReadFromJsonAsync<SalesInvoiceIssuanceValidationDto>())!;
        Assert.False(validation.IsReadyForIssuance); Assert.Contains(validation.Errors, x => x.Code == "SettlementAmountRequired"); Assert.Contains(validation.Errors, x => x.Code == "SettlementReasonRequired");
        using var approval = await browser.SendAsync(HttpMethod.Post, path + "/approve", new ApproveSalesInvoiceRequest(created.Version, created.Revision), tenantId: tenant); Assert.Equal(HttpStatusCode.BadRequest, approval.StatusCode);
        using var remove = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(created.Version, created.Draft with { Details = new() }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, remove.StatusCode); var cleared = (await remove.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Null(cleared.Draft.Details.Settlement); Assert.Null(cleared.SettlementAmounts); Assert.Equal(created.Totals, cleared.Totals);
    }

    [Fact]
    public async Task SalesInvoiceSettlementEnforcesTenantBoundaryAndDoesNotAcceptCalculatedFieldsAsInput()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var other = await f.CreateTenantAsync(); var foreign = await f.CreateTenantAsync(f.OtherId);
        var browser = f.Browser(); using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, tenant, SalesDraft() with { Details = new() { Settlement = SettlementInput() } }); var path = $"/api/sales-invoices/{created.Id}";
        using var wrongRead = await browser.SendAsync(HttpMethod.Get, path, tenantId: other); Assert.Equal(HttpStatusCode.NotFound, wrongRead.StatusCode);
        using var wrongEdit = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(created.Version, created.Draft), tenantId: other); Assert.Equal(HttpStatusCode.NotFound, wrongEdit.StatusCode);
        using var denied = await browser.SendAsync(HttpMethod.Get, path, tenantId: foreign); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var anonymous = await f.Browser().SendAsync(HttpMethod.Get, path, tenantId: tenant); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        var payload = JsonSerializer.SerializeToNode(created.Draft, new JsonSerializerOptions(JsonSerializerDefaults.Web))!; payload["details"]!["settlement"]!["amountDue"] = "1";
        using var injected = await browser.SendAsync(HttpMethod.Post, "/api/sales-invoices", payload, tenantId: tenant); Assert.Equal(HttpStatusCode.BadRequest, injected.StatusCode);
    }

    [Theory]
    [InlineData("precision")] [InlineData("count")] [InlineData("reason")] [InlineData("nullCollection")] [InlineData("nullEntry")]
    public async Task SalesInvoiceSettlementRejectsInvalidUpdatesWithoutPartialWrites(string problem)
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, tenant); var path = $"/api/sales-invoices/{created.Id}";
        IReadOnlyList<InvoiceSettlementEntryDto> charges = problem switch
        {
            "precision" => [new(new(.001m), "Powód")], "count" => Enumerable.Repeat(new InvoiceSettlementEntryDto(new(1m), "Powód"), 101).ToArray(),
            "reason" => [new(new(1m), new string('x', 257))], "nullCollection" => null!, _ => [null!]
        };
        using var update = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(created.Version, created.Draft with { Details = new() { Settlement = new() { Charges = charges } } }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.BadRequest, update.StatusCode);
        using var get = await browser.SendAsync(HttpMethod.Get, path, tenantId: tenant); var unchanged = (await get.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(created.Version, unchanged.Version); Assert.Equal(created.Revision, unchanged.Revision); Assert.Null(unchanged.SettlementAmounts);
    }

    [Fact]
    public async Task SalesInvoiceSettlementValidationDetectsWrongStoredResultsAndOnlyExplicitRecalculationRepairsThem()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var created = await CreateSalesAsync(browser, tenant, IssuanceDraft() with { NumberSeriesId = series.Id, Details = new() { Settlement = SettlementInput(deduction: 0) } });
        var wrong = new InvoiceSettlementAmounts(17, 0, 1, null);
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var invoice = await db.SalesInvoices.SingleAsync(x => x.Id == created.Id);
            db.Entry(invoice).Property(x => x.SettlementAmounts).CurrentValue = wrong; await db.SaveChangesAsync();
        }
        var path = $"/api/sales-invoices/{created.Id}";
        using var get = await browser.SendAsync(HttpMethod.Get, path, tenantId: tenant); var stored = (await get.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        using var validation = await browser.SendAsync(HttpMethod.Post, path + "/validate-issuance", tenantId: tenant);
        Assert.Contains((await validation.Content.ReadFromJsonAsync<SalesInvoiceIssuanceValidationDto>())!.Errors, x => x.Code == "SettlementAmountsMismatch");
        using var approval = await browser.SendAsync(HttpMethod.Post, path + "/approve", new ApproveSalesInvoiceRequest(stored.Version, stored.Revision), tenantId: tenant); Assert.Equal(HttpStatusCode.BadRequest, approval.StatusCode);
        using (var scope = f.Scope()) Assert.Equal(wrong, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().SalesInvoices.AsNoTracking().SingleAsync(x => x.Id == created.Id)).SettlementAmounts);
        using var recalculate = await browser.SendAsync(HttpMethod.Post, path + "/recalculate", new RecalculateSalesInvoiceRequest(stored.Version), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, recalculate.StatusCode); var repaired = (await recalculate.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(created.SettlementAmounts, repaired.SettlementAmounts); Assert.Equal(created.Totals, repaired.Totals);
    }
}
