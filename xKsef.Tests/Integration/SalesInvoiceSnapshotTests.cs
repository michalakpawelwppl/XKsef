using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Application.Contractors;
using xKsef.Application.SalesInvoices.Snapshots;
using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.SalesInvoices;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    [Fact]
    public async Task SalesInvoiceSnapshotExtendedExemptInvoiceKeepsAllBasesAndIgnoresLaterContractorAndTenantChanges()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        Guid contractor;
        using (var scope = f.Scope()) contractor = await scope.ServiceProvider.GetRequiredService<IContractorService>().CreateAsync(tenant,
            new() { FullName = "Pierwotny nabywca", ShortName = "P", Nip = "8567346215" });
        var draft = IssuanceDraft() with
        {
            NumberSeriesId = series.Id, IssuePlace = "Warszawa", SaleDate = null, SaleDateMode = (int)SaleDateMode.Period,
            SalePeriod = new(new(2026, 11, 1), new(2026, 11, 28)),
            Buyer = new() { SourceContractorId = contractor, BuyerLinkKey = "KL-1", CustomerNumber = "NUMER", TaxpayerStatus = (int)QualificationValue.Yes },
            Participants = [new() { Role = (int)InvoiceParticipantRole.Recipient, Party = new() { Name = "Odbiorca", IdentifierKind = 3 } }],
            Fa3 = new() { WarehouseDocumentNumbers = ["WZ/1", "WZ/2"], Qualifications = SalesInvoiceSnapshotTestData.ConfirmedQualificationsDto() with
                { HasExemptSales = new((int)QualificationValue.Yes, null, true) } },
            Lines = [new() { Description = "Usługa pierwsza", Unit = "szt.", Quantity = new(1), UnitPrice = new(100), TaxCode = "zw",
                TaxTreatment = new(1, (int)InvoiceTaxTreatmentKind.Exempt), ExemptionBasis = new(1, (int)ExemptionBasisKind.PolishLaw, "Pierwsza podstawa zwolnienia") },
                new() { Description = "Usługa druga", Unit = "szt.", Quantity = new(2), UnitPrice = new(50), TaxCode = "zw",
                ExemptionBasis = new(1, (int)ExemptionBasisKind.EuDirective, "Druga podstawa zwolnienia") }]
        };
        var created = await CreateSalesAsync(browser, tenant, draft);
        using var approvalResponse = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/approve", new ApproveSalesInvoiceRequest(created.Version, created.Revision), tenantId: tenant);
        Assert.True(approvalResponse.IsSuccessStatusCode, await approvalResponse.Content.ReadAsStringAsync());
        var approved = (await approvalResponse.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        string before;
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Set<SalesInvoiceApproval>().AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.SalesInvoiceId == created.Id);
            before = JsonSerializer.Serialize(row); var doc = scope.ServiceProvider.GetRequiredService<ISalesInvoiceApprovalSnapshotReader>().Read(row);
            Assert.Equal("Pierwotny nabywca", doc.Input!.Buyer!.Name);
            Assert.Equal(new[] { "Pierwsza podstawa zwolnienia", "Druga podstawa zwolnienia" }, doc.Input.Lines!.Select(x => x.ExemptionBasis!.Text));
            Assert.Equal(new int?[] { 0, 1 }, doc.Input.Lines!.Select(x => x.ExemptionBasis!.Kind));
            Assert.Equal("Odbiorca", Assert.Single(doc.Input.Participants!).Party!.Name); Assert.Equal(new[] { "WZ/1", "WZ/2" }, doc.Input.Fa3!.WarehouseDocumentNumbers);
            await scope.ServiceProvider.GetRequiredService<IContractorService>().UpdateAsync(tenant, contractor, new() { FullName = "Zmieniona kartoteka", ShortName = "Z", IsActive = true, Addresses = [] });
            await db.TaxEntities.Where(x => db.Tenants.Any(t => t.Id == tenant && t.TaxEntityId == x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.Name, "Nowa nazwa firmy"));
        }
        await f.RestartAsync();
        var reservation = await ReserveNumberAsync(f, tenant, NumberCommand(approved));
        var replay = await ReserveNumberAsync(f, tenant, NumberCommand(approved, reservation.OperationId)); Assert.Equal(reservation.Id, replay.Id);
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var row = await db.Set<SalesInvoiceApproval>().AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.SalesInvoiceId == created.Id);
            Assert.Equal(before, JsonSerializer.Serialize(row));
            var doc = scope.ServiceProvider.GetRequiredService<ISalesInvoiceApprovalSnapshotReader>().Read(row);
            Assert.Equal("Pierwotny nabywca", doc.Input!.Buyer!.Name); Assert.Equal(0m, doc.Calculations!.Totals!.Vat!.Value.Value);
            Assert.Equal(200m, doc.Calculations.Totals.Gross!.Value.Value);
        }
    }

    [Theory]
    [InlineData("unresolved")][InlineData("unconfirmed")][InlineData("margin")][InlineData("oss")][InlineData("selfBilling")][InlineData("transport")]
    public async Task SalesInvoiceSnapshotRejectsMissingQualificationsAndUnsupportedProceduresWithoutWrites(string reason)
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var draft = IssuanceDraft() with { NumberSeriesId = series.Id };
        if (reason is "unresolved" or "unconfirmed") draft = draft with { Fa3 = draft.Fa3 with { Qualifications = draft.Fa3.Qualifications with
            { CashAccounting = reason == "unresolved" ? new() : new(1, null, false) } } };
        if (reason == "margin") draft = draft with { Fa3 = draft.Fa3 with { Qualifications = draft.Fa3.Qualifications with { HasMarginSales = new(2, null, true) } } };
        if (reason == "oss") draft = draft with { Lines = [draft.Lines[0] with { TaxTreatment = new(1, (int)InvoiceTaxTreatmentKind.UnionOss, null, new(19), "DE") }] };
        if (reason == "selfBilling") draft = draft with { Fa3 = draft.Fa3 with { Qualifications = draft.Fa3.Qualifications with { SelfBilling = new(2, null, true) } } };
        if (reason == "transport") draft = draft with { Fa3 = draft.Fa3 with { Qualifications = draft.Fa3.Qualifications with { HasNewMeansOfTransport = new(2, null, true) } } };
        var created = await CreateSalesAsync(browser, tenant, draft);
        using var response = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/approve", new ApproveSalesInvoiceRequest(created.Version, created.Revision), tenantId: tenant);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var code = reason switch { "unresolved" => "QualificationUnresolved", "unconfirmed" => "QualificationNotConfirmed", "margin" => "MarginCalculationNotImplemented",
            "oss" => "TaxTreatmentCalculationNotImplemented", "selfBilling" => "SelfBillingNotSupported", _ => "NewTransportDataNotSupported" };
        Assert.Contains(code, await response.Content.ReadAsStringAsync());
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Set<SalesInvoiceApproval>().AnyAsync(x => x.TenantId == tenant && x.SalesInvoiceId == created.Id));
        var saved = await db.SalesInvoices.AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.Id == created.Id);
        Assert.Equal(created.Revision, saved.Revision); Assert.Equal(SalesInvoiceStatus.Draft, saved.Status); Assert.Equal(created.Version, Convert.ToBase64String(saved.RowVersion));
    }
}
