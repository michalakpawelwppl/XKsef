using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Application.Contractors;
using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.SalesInvoices;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    private static SalesInvoiceDraftDto Fa3Draft() => new()
    {
        ModelVersion = 2, IssuePlace = "Warszawa", PlannedIssueDate = new(2026, 11, 28), SaleDateMode = (int)SaleDateMode.Period, SalePeriod = new(null, new(2026, 11, 28)),
        Buyer = new() { Name = "Nabywca", IdentifierKind = 3, Eori = "EORI", BuyerLinkKey = "BUYER-KEY", CustomerNumber = "KL-1", TaxpayerStatus = (int)QualificationValue.Yes, Address = new("PL", "Adres", "Adres 2", "123"), CorrespondenceAddress = new("PL", "Korespondencja"), Contacts = [new("contact@example.com", "123456789")] },
        Participants = [new() { Role = (int)InvoiceParticipantRole.AdditionalBuyer, SharePercent = new(40.123456m), Party = new() { Name = "Uczestnik 1", IdentifierKind = 4, Identifier = "8567346215-00001", CustomerNumber = "P1" } }, new() { Role = (int)InvoiceParticipantRole.Other, OtherRoleDescription = "Rola dodatkowa", Party = new() { Name = "Uczestnik 2", IdentifierKind = 3 } }],
        Lines = [new() { Description = "Usługa", Unit = "szt.", Quantity = new(1.250000m), UnitPrice = new(12.12345678m), PriceBasis = 1, SaleDate = new(2026, 11, 27), Pkob = "1110", Procedure = (int)InvoiceProcedureCode.WSTO_EE, ExciseAmount = new(2.50m), Annex15 = new((int)QualificationValue.Yes, null, true), TaxTreatment = new(1, (int)InvoiceTaxTreatmentKind.UnionOss, null, new(19.123456m), "DE"), ExemptionBasis = new(1, (int)ExemptionBasisKind.PolishLaw, "Art. 43 ustawy") }],
        Fa3 = new()
        {
            WarehouseDocumentNumbers = ["WZ/2", "WZ/1"],
            Qualifications = new() { CashAccounting = new((int)QualificationValue.Yes, null, true, "Ocena użytkownika"), SplitPayment = new((int)QualificationValue.No, (int)QualificationValue.No, true), RelatedParties = new((int)QualificationValue.Unknown, (int)QualificationValue.Yes) },
            AuthorizedParty = new((int)AuthorizedPartyRole.TaxRepresentative, new() { Name = "Przedstawiciel", Identifier = "8567346215", Address = new("PL", "Adres PU"), CorrespondenceAddress = new("PL", "Korespondencja PU"), Eori = "PU-EORI" })
        }
    };

    [Fact]
    public async Task SalesInvoiceFa3RoundTripSurvivesRestartAndUpdatesEveryNewSection()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, tenant, Fa3Draft());
        Assert.False(created.CalculationComplete); Assert.Null(created.Totals.Gross);
        Assert.All(created.Draft.Participants, x => Assert.NotEqual(Guid.Empty, x.Id));
        Assert.Equal(f.UserId, created.QualificationDecisions!.Single(x => x.Path.EndsWith("CashAccounting", StringComparison.Ordinal)).ConfirmedBy);
        await f.RestartAsync();
        using var get = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        Assert.True(get.IsSuccessStatusCode, await get.Content.ReadAsStringAsync());
        var read = (await get.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(JsonSerializer.Serialize(created.Draft), JsonSerializer.Serialize(read.Draft));
        Assert.Equal(JsonSerializer.Serialize(created.QualificationDecisions), JsonSerializer.Serialize(read.QualificationDecisions));
        var changed = read.Draft with
        {
            IssuePlace = "Kraków", SaleDateMode = (int)SaleDateMode.PerLine, SalePeriod = null,
            Buyer = read.Draft.Buyer! with { BuyerLinkKey = null, CustomerNumber = "KL-2", SellerStatus = (int)SellerInformationStatus.Liquidation, PlPrefix = new((int)QualificationValue.No, null, true) },
            Participants = [read.Draft.Participants[1] with { OtherRoleDescription = "Zmiana roli" }, read.Draft.Participants[0] with { SharePercent = null }, new() { Party = new() { Name = "Nowy", IdentifierKind = 3 } }],
            Lines = [read.Draft.Lines[0] with { TaxTreatment = null, ExemptionBasis = new(), Pkob = null, Procedure = null, ExciseAmount = null, SaleDate = null, Annex15 = new() }],
            Fa3 = read.Draft.Fa3 with { WarehouseDocumentNumbers = ["WZ/3"], AuthorizedParty = null }
        };
        using var put = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{read.Id}", new UpdateSalesInvoiceRequest(read.Version, changed), tenantId: tenant);
        Assert.True(put.IsSuccessStatusCode, await put.Content.ReadAsStringAsync());
        var updated = (await put.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(read.Revision + 1, updated.Revision); Assert.NotEqual(read.Version, updated.Version);
        Assert.Equal(read.Draft.Participants[1].Id, updated.Draft.Participants[0].Id);
        Assert.Null(updated.QualificationDecisions!.Single(x => x.Path.EndsWith("CashAccounting", StringComparison.Ordinal)).ConfirmedBy);
        using var reread = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{updated.Id}", tenantId: tenant);
        var saved = (await reread.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Null(saved.Draft.SalePeriod); Assert.Null(saved.Draft.Lines[0].TaxTreatment);
        Assert.NotNull(saved.Draft.Lines[0].ExemptionBasis); Assert.Null(saved.Draft.Lines[0].ExemptionBasis!.Kind); Assert.Null(saved.Draft.Lines[0].ExemptionBasis!.Text);
        Assert.Null(saved.Draft.Fa3.AuthorizedParty); Assert.Null(saved.Draft.Buyer!.BuyerLinkKey);
        Assert.Equal("KL-2", saved.Draft.Buyer.CustomerNumber); Assert.Equal((int)SellerInformationStatus.Liquidation, saved.Draft.Buyer.SellerStatus);
        Assert.Equal(new[] { "Uczestnik 2", "Uczestnik 1", "Nowy" }, saved.Draft.Participants.Select(x => x.Party.Name));
        Assert.Equal("WZ/3", Assert.Single(saved.Draft.Fa3.WarehouseDocumentNumbers));
        Assert.Null(saved.Draft.Participants[1].SharePercent);
    }

    [Fact]
    public async Task SalesInvoiceFa3TenantReferencesAndDatabaseParticipantLinksAreProtected()
    {
        await using var f = await Fixture.CreateAsync(); var a = await f.CreateTenantAsync(); var b = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, a, Fa3Draft());
        using var other = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: b);
        Assert.Equal(HttpStatusCode.NotFound, other.StatusCode);
        using var foreignId = await browser.SendAsync(HttpMethod.Post, "/api/sales-invoices", Fa3Draft() with { Participants = created.Draft.Participants }, tenantId: b);
        Assert.Equal(HttpStatusCode.BadRequest, foreignId.StatusCode);
        Guid source;
        using (var scope = f.Scope()) source = await scope.ServiceProvider.GetRequiredService<IContractorService>().CreateAsync(b, new() { FullName = "Obcy kontrahent", ShortName = "Obcy" });
        using var badSource = await browser.SendAsync(HttpMethod.Post, "/api/sales-invoices", Fa3Draft() with { Participants = [new() { Party = new() { SourceContractorId = source } }] }, tenantId: a);
        Assert.Equal(HttpStatusCode.BadRequest, badSource.StatusCode);
        using var badAuthorized = await browser.SendAsync(HttpMethod.Post, "/api/sales-invoices", Fa3Draft() with { Fa3 = new() { AuthorizedParty = new(null, new() { SourceContractorId = source }) } }, tenantId: a);
        Assert.Equal(HttpStatusCode.BadRequest, badAuthorized.StatusCode);
        using var sqlScope = f.Scope(); var db = sqlScope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SalesInvoiceParticipants SET TenantId={b} WHERE SalesInvoiceId={created.Id} AND TenantId={a}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SalesInvoices SET Fa3={"{\"Version\":999}"} WHERE Id={created.Id} AND TenantId={a}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SalesInvoices SET Fa3={"{}"} WHERE Id={created.Id} AND TenantId={a}"));
        Assert.Equal(2, await db.Set<SalesInvoiceParticipant>().CountAsync(x => x.TenantId == a && x.SalesInvoiceId == created.Id));
    }

    [Fact]
    public async Task SalesInvoiceFa3ConcurrentEditsCommitOneCoherentAggregate()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync(); var created = await CreateSalesAsync(browser, tenant, Fa3Draft());
        var a = created.Draft with { IssuePlace = "A", Participants = created.Draft.Participants.Reverse().ToArray() };
        var b = created.Draft with { IssuePlace = "B", Participants = [created.Draft.Participants[0]] };
        var results = await Task.WhenAll(browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(created.Version, a), tenantId: tenant), browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(created.Version, b), tenantId: tenant));
        try
        {
            Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict);
            var winner = (await results.Single(x => x.IsSuccessStatusCode).Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
            using var get = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
            var saved = (await get.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
            Assert.Equal(created.Revision + 1, saved.Revision); Assert.Equal(winner.Version, saved.Version);
            Assert.Equal(winner.Draft.IssuePlace, saved.Draft.IssuePlace);
            Assert.Equal(winner.Draft.Participants.Select(x => x.Id), saved.Draft.Participants.Select(x => x.Id));
        }
        finally { foreach (var result in results) result.Dispose(); }
    }

    [Fact]
    public async Task SalesInvoiceFa3IncompleteDraftStaysUnapprovedWhileAllVatMethodsCalculate()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using (var permissions = f.Scope()) await permissions.ServiceProvider.GetRequiredService<AppDbContext>().UserTenants.Where(x => x.TenantId == tenant && x.UserId == f.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, "Admin"));
        using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, tenant, new() { ModelVersion = 2, SalePeriod = new(null, null), Participants = [new()], Lines = [new() { TaxTreatment = new(), ExemptionBasis = new() }] });
        using var get = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        var read = (await get.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.NotNull(read.Draft.SalePeriod); Assert.Null(read.Draft.SalePeriod!.From); Assert.Null(read.Draft.SalePeriod.To);
        Assert.Null(read.Draft.Participants[0].Role); Assert.NotNull(read.Draft.Lines[0].TaxTreatment);
        Assert.Equal((int)QualificationValue.Unknown, read.Draft.Fa3.Qualifications.SplitPayment.Value);
        using var approval = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{read.Id}/approve", new ApproveSalesInvoiceRequest(read.Version, read.Revision), tenantId: tenant);
        Assert.Equal(HttpStatusCode.BadRequest, approval.StatusCode); Assert.Contains("QualificationUnresolved", await approval.Content.ReadAsStringAsync());
        foreach (var method in new[] { VatCalculationMethod.PerLineNet, VatCalculationMethod.PerLineGross })
        {
            var pending = await CreateSalesAsync(browser, tenant, SalesDraft() with { VatMethod = (int)method });
            Assert.True(pending.CalculationComplete); Assert.NotNull(pending.Totals.Net); Assert.NotNull(pending.Totals.Vat); Assert.NotNull(pending.Totals.Gross);
            using var denied = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{pending.Id}/approve", new ApproveSalesInvoiceRequest(pending.Version, pending.Revision), tenantId: tenant);
            Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode); Assert.DoesNotContain("VatMethodNotImplemented", await denied.Content.ReadAsStringAsync());
        }
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Set<SalesInvoiceApproval>().AnyAsync(x => x.TenantId == tenant));
    }

    [Fact]
    public async Task SalesInvoiceFa3AuditCannotBeSuppliedByTheClient()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var forged = JsonSerializer.Deserialize<JsonElement>("""{"fa3":{"qualifications":{"splitPayment":{"value":2,"confirm":true,"confirmedBy":"attacker","source":3}}}}""");
        using var rejected = await browser.SendAsync(HttpMethod.Post, "/api/sales-invoices", forged, tenantId: tenant);
        Assert.Equal(HttpStatusCode.BadRequest, rejected.StatusCode);
        var created = await CreateSalesAsync(browser, tenant, Fa3Draft());
        Assert.All(created.QualificationDecisions!.Where(x => x.ConfirmedBy is not null), x => Assert.Equal(f.UserId, x.ConfirmedBy));
    }
}
