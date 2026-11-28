using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Contracts.SalesInvoices;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    private static SalesInvoiceDraftDto IssuanceDraft() => SalesDraft() with
    {
        SaleDateMode = 1, Fa3 = new() { Qualifications = SalesInvoiceSnapshotTestData.ConfirmedQualificationsDto() },
        Seller = new() { Name = "Sprzedawca", Address = new("PL", "Warszawa, ul. Testowa 1") },
        Buyer = new() { Name = "Konsument", IdentifierKind = 3, Address = new("PL", "Kraków, ul. Testowa 2") }
    };

    [Fact]
    public async Task IssuanceValidationReadsLatestPersistedDraftAndNeverChangesIt()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        var series = xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries.Create(tenant, new("FV", "FV", "{prefix}/{number}/{year}", 6, xKsef.Domain.InvoiceNumberSeries.NumberSeriesResetMode.Yearly), f.UserId, f.Clock.GetUtcNow());
        // The shared fixture generates arbitrary NIPs; this positive scenario needs a valid checksum.
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var entity = await db.TaxEntities.SingleAsync(x => db.Tenants.Any(t => t.Id == tenant && t.TaxEntityId == x.Id));
            entity.Nip = "8567346215";
            db.Set<xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries>().Add(series);
            await db.SaveChangesAsync();
        }
        using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, tenant, IssuanceDraft() with { NumberSeriesId = series.Id });
        using var before = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        var snapshot = await before.Content.ReadAsStringAsync();
        var path = $"/api/sales-invoices/{created.Id}/validate-issuance";
        using var response = await browser.SendAsync(HttpMethod.Post, path, tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<SalesInvoiceIssuanceValidationDto>())!;
        Assert.True(result.IsReadyForIssuance, JsonSerializer.Serialize(result)); Assert.Empty(result.Errors);
        using var get = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        Assert.Equal(snapshot, await get.Content.ReadAsStringAsync());

        using var update = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(created.Version, created.Draft with { Buyer = null }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode);
        using var beforeInvalid = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        var updatedSnapshot = await beforeInvalid.Content.ReadAsStringAsync();
        using var invalidResponse = await browser.SendAsync(HttpMethod.Post, path, tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, invalidResponse.StatusCode);
        var invalid = (await invalidResponse.Content.ReadFromJsonAsync<SalesInvoiceIssuanceValidationDto>())!;
        Assert.False(invalid.IsReadyForIssuance); Assert.Contains(invalid.Errors, x => x.Path == "buyer" && x.Code == "PartyRequired");
        using var after = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        Assert.Equal(updatedSnapshot, await after.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task IssuanceValidationReturnsAllErrorsForSavedIncompleteDraft()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, tenant, new());
        using var response = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/validate-issuance", tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = (await response.Content.ReadFromJsonAsync<SalesInvoiceIssuanceValidationDto>())!;
        Assert.False(result.IsReadyForIssuance);
        foreach (var code in new[] { "PartyRequired", "AddressRequired", "IssueDateRequired", "LinesRequired", "CalculationIncomplete" }) Assert.Contains(result.Errors, x => x.Code == code);
    }

    [Fact]
    public async Task IssuanceValidationEnforcesTenantBoundaryAuthenticationAndCsrf()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var another = await f.CreateTenantAsync(); var foreign = await f.CreateTenantAsync(f.OtherId);
        var browser = f.Browser(); using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, tenant, IssuanceDraft()); var path = $"/api/sales-invoices/{created.Id}/validate-issuance";
        using var wrongTenant = await browser.SendAsync(HttpMethod.Post, path, tenantId: another); Assert.Equal(HttpStatusCode.NotFound, wrongTenant.StatusCode);
        using var denied = await browser.SendAsync(HttpMethod.Post, path, tenantId: foreign); Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var missing = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{Guid.NewGuid()}/validate-issuance", tenantId: tenant); Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        var anonymous = f.Browser();
        using var unauthorized = await anonymous.SendAsync(HttpMethod.Post, path, tenantId: tenant); Assert.Equal(HttpStatusCode.Unauthorized, unauthorized.StatusCode);
        browser.Token = null;
        using var csrf = await browser.SendAsync(HttpMethod.Post, path, tenantId: tenant); Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
    }
}
