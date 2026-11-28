using System.Net;
using System.Net.Http.Json;
using System.Numerics;
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
    private static SalesInvoiceDetailsDto PaymentDetails() => new()
    {
        PaymentStatus = (int)InvoicePaymentStatus.PartiallyPaid,
        PartialPayments = [new(new(10m), new(2026, 11, 27), 6), new(new(5m), new(2026, 11, 28), 8, "Inna forma")],
        PaymentMethod = 6, PaymentTerms = [new(null, BigInteger.Parse("123456789012345678901234567890"), "dni", "zdarzenie")],
        CashDiscount = new("Płatność w ciągu 7 dni", "3% brutto"), PaymentLink = "https://pay.example/?IPKSeF=001ABC123DEF4", KsefPaymentId = "001ABC123DEF4"
    };

    [Fact]
    public async Task SalesInvoicePaymentRoundTripUpdateApprovalSnapshotAndNumberingPreserveAllData()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var created = await CreateSalesAsync(browser, tenant, IssuanceDraft() with { NumberSeriesId = series.Id, Details = PaymentDetails() with
        { Settlement = SettlementInput(deduction: 1m) } });
        var path = $"/api/sales-invoices/{created.Id}";
        Assert.Equal(2, created.Draft.Details.PartialPayments.Count); Assert.Equal(PaymentDetails().PaymentTerms[0].Quantity, created.Draft.Details.PaymentTerms[0].Quantity);
        Assert.Equal(created.Totals.Gross!.Value.Value + 16m, created.SettlementAmounts!.AmountDue!.Value.Value); // Payments are not deductions.
        await f.RestartAsync();
        using var get = await browser.SendAsync(HttpMethod.Get, path, tenantId: tenant); var stored = (await get.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(created.Draft.Details.PartialPayments, stored.Draft.Details.PartialPayments);
        Assert.Equal(created.Draft.Details.CashDiscount, stored.Draft.Details.CashDiscount); Assert.Equal(created.Draft.Details.PaymentLink, stored.Draft.Details.PaymentLink);
        var edited = stored.Draft with { Details = stored.Draft.Details with { PartialPayments = stored.Draft.Details.PartialPayments.Reverse().ToArray(), CashDiscount = new("14 dni", "2% brutto") } };
        using var update = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(stored.Version, edited), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode); var current = (await update.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(stored.Totals, current.Totals); Assert.Equal(stored.SettlementAmounts, current.SettlementAmounts); Assert.Equal(stored.Revision + 1, current.Revision);
        using var stale = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(stored.Version, stored.Draft), tenantId: tenant); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var staleApproval = await browser.SendAsync(HttpMethod.Post, path + "/approve", new ApproveSalesInvoiceRequest(current.Version, stored.Revision), tenantId: tenant); Assert.Equal(HttpStatusCode.Conflict, staleApproval.StatusCode);
        using var confirm = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(current.Version, current.Draft with { Fa3 = current.Draft.Fa3 with { Qualifications = SalesInvoiceSnapshotTestData.ConfirmedQualificationsDto() } }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode); current = (await confirm.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        using var approval = await browser.SendAsync(HttpMethod.Post, path + "/approve", new ApproveSalesInvoiceRequest(current.Version, current.Revision), tenantId: tenant);
        Assert.True(approval.IsSuccessStatusCode, await approval.Content.ReadAsStringAsync()); var approved = (await approval.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        string json;
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>(); var row = await db.Set<SalesInvoiceApproval>().AsNoTracking().SingleAsync(x => x.SalesInvoiceId == created.Id && x.TenantId == tenant); json = row.SnapshotJson;
            var snapshot = scope.ServiceProvider.GetRequiredService<ISalesInvoiceApprovalSnapshotReader>().Read(row); var details = snapshot.Input!.Details!;
            Assert.Equal((int)InvoicePaymentStatus.PartiallyPaid, details.PaymentStatus); Assert.Equal(5m, details.PartialPayments![0].Amount!.Value.Value);
            Assert.Equal("Inna forma", details.PartialPayments[0].OtherPaymentDescription); Assert.Equal("2% brutto", details.CashDiscount!.AmountDescription);
            Assert.Equal(PaymentDetails().PaymentTerms[0].Quantity, details.PaymentTerms![0].Quantity);
            Assert.Equal(PaymentDetails().PaymentLink, details.PaymentLink); Assert.Equal(PaymentDetails().KsefPaymentId, details.KsefPaymentId);
        }
        using var blocked = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(approved.Version, stored.Draft), tenantId: tenant); Assert.Equal(HttpStatusCode.Conflict, blocked.StatusCode);
        var reservation = await ReserveNumberAsync(f, tenant, NumberCommand(approved)); Assert.Equal(approved.Approval!.Id, reservation.ApprovalId);
        using (var scope = f.Scope()) Assert.Equal(json, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<SalesInvoiceApproval>().AsNoTracking().SingleAsync(x => x.Id == approved.Approval.Id)).SnapshotJson);
    }

    [Fact]
    public async Task SalesInvoicePaymentSinglePaymentAndIncompleteDraftsHaveDifferentApprovalReadiness()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var created = await CreateSalesAsync(browser, tenant, IssuanceDraft() with { NumberSeriesId = series.Id, Details = new() { PaymentStatus = (int)InvoicePaymentStatus.PaidOnce, CashDiscount = new() } });
        var path = $"/api/sales-invoices/{created.Id}";
        using var check = await browser.SendAsync(HttpMethod.Post, path + "/validate-issuance", tenantId: tenant); var validation = (await check.Content.ReadFromJsonAsync<SalesInvoiceIssuanceValidationDto>())!;
        Assert.False(validation.IsReadyForIssuance); Assert.Contains(validation.Errors, x => x.Code == "PaymentDateRequired"); Assert.Contains(validation.Errors, x => x.Code == "PaymentFieldRequired");
        using var invalidApproval = await browser.SendAsync(HttpMethod.Post, path + "/approve", new ApproveSalesInvoiceRequest(created.Version, created.Revision), tenantId: tenant); Assert.Equal(HttpStatusCode.BadRequest, invalidApproval.StatusCode);
        var complete = await CreateSalesAsync(browser, tenant, IssuanceDraft() with { NumberSeriesId = series.Id, Details = new() { PaymentStatus = (int)InvoicePaymentStatus.PaidOnce, PaymentDate = new(2026, 11, 27), PaymentMethod = 6 } });
        using var approval = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{complete.Id}/approve", new ApproveSalesInvoiceRequest(complete.Version, complete.Revision), tenantId: tenant);
        Assert.True(approval.IsSuccessStatusCode, await approval.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SalesInvoicePaymentTenantAndAuthenticationBoundariesApplyToNewFields()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var other = await f.CreateTenantAsync(); var foreign = await f.CreateTenantAsync(f.OtherId);
        var browser = f.Browser(); using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, tenant, new() { Details = PaymentDetails() }); var path = $"/api/sales-invoices/{created.Id}";
        using var wrong = await browser.SendAsync(HttpMethod.Get, path, tenantId: other); Assert.Equal(HttpStatusCode.NotFound, wrong.StatusCode);
        using var wrongEdit = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(created.Version, created.Draft), tenantId: other); Assert.Equal(HttpStatusCode.NotFound, wrongEdit.StatusCode);
        using var denied = await browser.SendAsync(HttpMethod.Get, path, tenantId: foreign); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var anonymous = await f.Browser().SendAsync(HttpMethod.Get, path, tenantId: tenant); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    [Theory]
    [InlineData("count")] [InlineData("amount")] [InlineData("link")] [InlineData("id")] [InlineData("status")] [InlineData("nullCollection")] [InlineData("nullEntry")]
    public async Task SalesInvoicePaymentInvalidStructureDoesNotChangePersistedDraft(string problem)
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, tenant); var path = $"/api/sales-invoices/{created.Id}";
        var details = problem switch
        {
            "count" => new SalesInvoiceDetailsDto { PartialPayments = Enumerable.Repeat(new PartialPaymentDto(new(1m)), 101).ToArray() },
            "amount" => new() { PartialPayments = [new(new(.001m))] }, "link" => new() { PaymentLink = "https://pay.example/" },
            "id" => new() { KsefPaymentId = "invalid" }, "status" => new() { PaymentStatus = 0 },
            "nullCollection" => new() { PartialPayments = null! }, _ => new() { PartialPayments = [null!] }
        };
        using var response = await browser.SendAsync(HttpMethod.Put, path, new UpdateSalesInvoiceRequest(created.Version, created.Draft with { Details = details }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var get = await browser.SendAsync(HttpMethod.Get, path, tenantId: tenant); var same = (await get.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(created.Version, same.Version); Assert.Equal(created.Revision, same.Revision); Assert.Empty(same.Draft.Details.PartialPayments); Assert.Null(same.Draft.Details.PaymentStatus);
    }
}
