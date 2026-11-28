using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.SalesInvoices;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    [Fact]
    public async Task SalesInvoiceUnapprovalAllowsEditingAndReapprovalReplacesTheOnlySnapshot()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var approved = await NumberingInvoiceAsync(browser, tenant, series.Id);
        var oldId = approved.Approval!.Id;
        using var response = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{approved.Id}/unapprove", new ApproveSalesInvoiceRequest(approved.Version, approved.Revision), tenantId: tenant);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var draft = (await response.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(0, draft.Status); Assert.Null(draft.Approval); Assert.Equal(approved.Revision + 1, draft.Revision); Assert.NotEqual(approved.Version, draft.Version);
        using var edit = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{draft.Id}", new UpdateSalesInvoiceRequest(draft.Version, draft.Draft with { Notes = "Nowa treść" }), tenantId: tenant);
        Assert.True(edit.IsSuccessStatusCode, await edit.Content.ReadAsStringAsync()); draft = (await edit.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        using var confirm = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{draft.Id}", new UpdateSalesInvoiceRequest(draft.Version,
            draft.Draft with { Fa3 = draft.Draft.Fa3 with { Qualifications = SalesInvoiceSnapshotTestData.ConfirmedQualificationsDto() } }), tenantId: tenant);
        Assert.True(confirm.IsSuccessStatusCode, await confirm.Content.ReadAsStringAsync()); draft = (await confirm.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        using var again = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{draft.Id}/approve", new ApproveSalesInvoiceRequest(draft.Version, draft.Revision), tenantId: tenant);
        Assert.True(again.IsSuccessStatusCode, await again.Content.ReadAsStringAsync()); var current = (await again.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.NotEqual(oldId, current.Approval!.Id); Assert.Equal(approved.Totals, current.Totals);
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var row = Assert.Single(await db.Set<SalesInvoiceApproval>().Where(x => x.TenantId == tenant && x.SalesInvoiceId == current.Id).ToArrayAsync());
        Assert.Equal(current.Approval.Id, row.Id); Assert.DoesNotContain(oldId.ToString(), row.SnapshotJson);
        Assert.Contains("Nowa treść", System.Text.Json.JsonSerializer.Deserialize<xKsef.Application.SalesInvoices.Snapshots.SalesInvoiceApprovalSnapshot>(row.SnapshotJson)!.Input!.Notes);
        var reservation = await ReserveNumberAsync(f, tenant, NumberCommand(current)); Assert.Equal(current.Approval.Id, reservation.ApprovalId);
    }

    [Theory]
    [InlineData("active")] [InlineData("uncertain")] [InlineData("released")]
    public async Task SalesInvoiceUnapprovalRejectsEveryStartedOrUncertainNumberingOperation(string state)
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var invoice = await NumberingInvoiceAsync(browser, tenant, series.Id); var number = await ReserveNumberAsync(f, tenant, NumberCommand(invoice));
        if (state == "uncertain") await NumberActionAsync(f, s => s.MarkUncertainAsync(tenant, invoice.Id, number.OperationId, f.UserId, "Timeout"));
        if (state == "released") await ResolveTestNumberAsync(f, tenant, number, false);
        using var response = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{invoice.Id}/unapprove", new ApproveSalesInvoiceRequest(invoice.Version, invoice.Revision), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.True(await db.Set<SalesInvoiceApproval>().AnyAsync(x => x.Id == invoice.Approval!.Id && x.TenantId == tenant));
        Assert.Equal(SalesInvoiceStatus.Approved, (await db.SalesInvoices.SingleAsync(x => x.Id == invoice.Id)).Status);
    }

    [Fact]
    public async Task SalesInvoiceUnapprovalRequiresPermissionTenantAndCurrentVersion()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var invoice = await NumberingInvoiceAsync(browser, tenant, series.Id); var other = await f.CreateTenantAsync();
        using (var scope = f.Scope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserTenants.Where(x => x.TenantId == other && x.UserId == f.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, "Admin"));
        var path = $"/api/sales-invoices/{invoice.Id}/unapprove";
        using var foreign = await browser.SendAsync(HttpMethod.Post, path, new ApproveSalesInvoiceRequest(invoice.Version, invoice.Revision), tenantId: other); Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var stale = await browser.SendAsync(HttpMethod.Post, path, new ApproveSalesInvoiceRequest(invoice.Version, invoice.Revision - 1), tenantId: tenant); Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using (var scope = f.Scope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserTenants.Where(x => x.TenantId == tenant && x.UserId == f.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, "User"));
        using var denied = await browser.SendAsync(HttpMethod.Post, path, new ApproveSalesInvoiceRequest(invoice.Version, invoice.Revision), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Contains("SalesInvoices.Unapprove", await denied.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SalesInvoiceConcurrentUnapprovalsHaveOneWinnerAndRemoveTheSnapshotAtomically()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var invoice = await NumberingInvoiceAsync(browser, tenant, series.Id); var path = $"/api/sales-invoices/{invoice.Id}/unapprove";
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => browser.SendAsync(HttpMethod.Post, path, new ApproveSalesInvoiceRequest(invoice.Version, invoice.Revision), tenantId: tenant)));
        try { Assert.Single(results, x => x.StatusCode == HttpStatusCode.OK); Assert.Single(results, x => x.StatusCode == HttpStatusCode.Conflict); }
        finally { foreach (var result in results) result.Dispose(); }
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Set<SalesInvoiceApproval>().AnyAsync(x => x.TenantId == tenant && x.SalesInvoiceId == invoice.Id));
        Assert.Equal(SalesInvoiceStatus.Draft, (await db.SalesInvoices.SingleAsync(x => x.Id == invoice.Id)).Status);
    }

    [Fact]
    public async Task SalesInvoiceUnapprovalAndNumberReservationCannotBothWin()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var invoice = await NumberingInvoiceAsync(browser, tenant, series.Id);
        var reservation = Record.ExceptionAsync(async () => { await ReserveNumberAsync(f, tenant, NumberCommand(invoice)); });
        using var response = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{invoice.Id}/unapprove", new ApproveSalesInvoiceRequest(invoice.Version, invoice.Revision), tenantId: tenant);
        var error = await reservation;
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var saved = await db.SalesInvoices.SingleAsync(x => x.TenantId == tenant && x.Id == invoice.Id);
        if (response.StatusCode == HttpStatusCode.OK)
        {
            Assert.IsType<xKsef.Domain.InvoiceNumberSeries.InvoiceNumberingConflictException>(error);
            Assert.Equal(SalesInvoiceStatus.Draft, saved.Status);
            Assert.False(await db.Set<xKsef.Domain.InvoiceNumberSeries.InvoiceNumberReservation>().AnyAsync(x => x.TenantId == tenant && x.InvoiceId == invoice.Id));
        }
        else { Assert.Equal(HttpStatusCode.Conflict, response.StatusCode); Assert.Null(error); Assert.Equal(SalesInvoiceStatus.Approved, saved.Status); }
    }

    [Fact]
    public async Task SalesInvoiceApprovedSnapshotCannotBeChangedThroughTrackedPersistence()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var invoice = await NumberingInvoiceAsync(browser, tenant, series.Id);
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var approval = await db.Set<SalesInvoiceApproval>().SingleAsync(x => x.TenantId == tenant && x.SalesInvoiceId == invoice.Id);
        var original = approval.SnapshotJson;
        db.Entry(approval).Property(x => x.SnapshotJson).CurrentValue = "{}";
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync()); db.ChangeTracker.Clear();
        Assert.Equal(original, await db.Set<SalesInvoiceApproval>().Where(x => x.Id == approval.Id).Select(x => x.SnapshotJson).SingleAsync());
    }
}
