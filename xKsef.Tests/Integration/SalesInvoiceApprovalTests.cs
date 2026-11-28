using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Application.SalesInvoices;
using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.SalesInvoices;
using xKsef.Infrastructure.Persistence;
using xKsef.Infrastructure.Persistence.Repositories;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    private static async Task<(Guid Tenant, Browser Browser, SalesInvoiceDto Invoice)> ApprovalScenarioAsync(Fixture f, bool admin = true, bool complete = true)
    {
        var tenant = await f.CreateTenantAsync();
        var series = xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries.Create(tenant, new("FV", "FV", "{prefix}/{number}/{year}", 6, xKsef.Domain.InvoiceNumberSeries.NumberSeriesResetMode.Yearly), f.UserId, f.Clock.GetUtcNow());
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Set<xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries>().Add(series); await db.SaveChangesAsync();
            await db.TaxEntities.Where(x => db.Tenants.Any(t => t.Id == tenant && t.TaxEntityId == x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.Nip, "8567346215"));
            if (admin) await db.UserTenants.Where(x => x.TenantId == tenant && x.UserId == f.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, "Admin"));
        }
        var browser = f.Browser(); using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var invoice = await CreateSalesAsync(browser, tenant, (complete ? IssuanceDraft() : new()) with { NumberSeriesId = series.Id });
        return (tenant, browser, invoice);
    }

    [Fact]
    public async Task SalesInvoiceApprovalRecordsExactSourceAndBlocksFurtherWritesWithoutIssuing()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, created) = await ApprovalScenarioAsync(f);
        using var before = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        var source = (await before.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        f.Clock.Advance(TimeSpan.FromMinutes(2)); var expectedTime = f.Clock.GetUtcNow();
        using var response = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/approve", new ApproveSalesInvoiceRequest(created.Version, created.Revision), tenantId: tenant);
        Assert.True(response.StatusCode == HttpStatusCode.OK, await response.Content.ReadAsStringAsync());
        var approved = (await response.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal((int)SalesInvoiceStatus.Approved, approved.Status); Assert.Equal(created.Revision + 1, approved.Revision); Assert.NotEqual(created.Version, approved.Version);
        Assert.NotNull(approved.Approval); Assert.Equal(f.UserId, approved.Approval.ApprovedBy); Assert.Equal(expectedTime, approved.Approval.ApprovedAt);
        Assert.Equal(created.Revision, approved.Approval.SourceRevision); Assert.Equal(approved.Revision, approved.Approval.ApprovedRevision);
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var eventRow = await db.Set<SalesInvoiceApproval>().AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.SalesInvoiceId == created.Id);
            var snapshot = scope.ServiceProvider.GetRequiredService<xKsef.Application.SalesInvoices.Snapshots.ISalesInvoiceApprovalSnapshotReader>().Read(eventRow);
            Assert.Equal(source.Revision, snapshot.SourceRevision); Assert.Equal(source.Version, snapshot.Metadata!.SourceRowVersion);
            Assert.Equal(source.Totals.Gross!.Value.Value, snapshot.Calculations!.Totals!.Gross!.Value.Value);
            Assert.False(await db.Invoices.AnyAsync(x => x.TenantId == tenant));
        }
        using var repeat = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/approve", new ApproveSalesInvoiceRequest(approved.Version, approved.Revision), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Conflict, repeat.StatusCode);
        using var edit = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(approved.Version, created.Draft with { Notes = "Zmiana" }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Conflict, edit.StatusCode);
        using var recalc = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/recalculate", new RecalculateSalesInvoiceRequest(approved.Version), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Conflict, recalc.StatusCode);
        using var read = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        Assert.Equal(JsonSerializer.Serialize(approved), JsonSerializer.Serialize((await read.Content.ReadFromJsonAsync<SalesInvoiceDto>())!));
    }

    [Fact]
    public async Task SalesInvoiceApprovalRequiresPermissionAndReadsCurrentTenantRole()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, created) = await ApprovalScenarioAsync(f, admin: false);
        var path = $"/api/sales-invoices/{created.Id}/approve"; var body = new ApproveSalesInvoiceRequest(created.Version, created.Revision);
        using var denied = await browser.SendAsync(HttpMethod.Post, path, body, tenantId: tenant);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode); Assert.Contains("SalesInvoices.Issue", await denied.Content.ReadAsStringAsync());
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Empty(await db.Set<SalesInvoiceApproval>().Where(x => x.TenantId == tenant).ToArrayAsync());
            await db.UserTenants.Where(x => x.TenantId == tenant && x.UserId == f.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, "Admin"));
        }
        // Existing cookie remains in use; the newly granted permission comes from the tenant database.
        using var granted = await browser.SendAsync(HttpMethod.Post, path, body, tenantId: tenant); Assert.Equal(HttpStatusCode.OK, granted.StatusCode);
        using (var scope = f.Scope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserTenants.Where(x => x.TenantId == tenant && x.UserId == f.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, "User"));
        using var revoked = await browser.SendAsync(HttpMethod.Post, path, body, tenantId: tenant); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode);
    }

    [Fact]
    public async Task SalesInvoiceApprovalRejectsIncompleteInvoiceWithoutChangingVersionOrStatus()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, created) = await ApprovalScenarioAsync(f, complete: false);
        using var response = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/approve", new ApproveSalesInvoiceRequest(created.Version, created.Revision), tenantId: tenant);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var errors = JsonDocument.Parse(await response.Content.ReadAsStringAsync()); Assert.True(errors.RootElement.GetProperty("issues").GetArrayLength() > 1);
        using var read = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        var actual = (await read.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(created.Version, actual.Version); Assert.Equal(created.Revision, actual.Revision); Assert.Equal(0, actual.Status); Assert.Null(actual.Approval);
    }

    [Fact]
    public async Task SalesInvoiceApprovalRejectsForeignTenantAnonymousAndMissingCsrfRequests()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, created) = await ApprovalScenarioAsync(f);
        var another = await f.CreateTenantAsync(); var foreign = await f.CreateTenantAsync(f.OtherId);
        using (var scope = f.Scope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserTenants.Where(x => x.TenantId == another && x.UserId == f.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, "Admin"));
        var path = $"/api/sales-invoices/{created.Id}/approve"; var body = new ApproveSalesInvoiceRequest(created.Version, created.Revision);
        using var wrongTenant = await browser.SendAsync(HttpMethod.Post, path, body, tenantId: another); Assert.Equal(HttpStatusCode.NotFound, wrongTenant.StatusCode);
        using var forbidden = await browser.SendAsync(HttpMethod.Post, path, body, tenantId: foreign); Assert.Equal(HttpStatusCode.Forbidden, forbidden.StatusCode);
        using var anonymous = await f.Browser().SendAsync(HttpMethod.Post, path, body, tenantId: tenant); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        browser.Token = null;
        using var csrf = await browser.SendAsync(HttpMethod.Post, path, body, tenantId: tenant); Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
        using var scopeCheck = f.Scope(); Assert.False(await scopeCheck.ServiceProvider.GetRequiredService<AppDbContext>().Set<SalesInvoiceApproval>().AnyAsync(x => x.TenantId == tenant));
    }

    [Fact]
    public async Task SalesInvoiceApprovalRejectsStaleRevisionAndRowVersion()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, created) = await ApprovalScenarioAsync(f);
        var path = $"/api/sales-invoices/{created.Id}/approve";
        using var wrongRevision = await browser.SendAsync(HttpMethod.Post, path, new ApproveSalesInvoiceRequest(created.Version, created.Revision + 1), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Conflict, wrongRevision.StatusCode);
        using var edited = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(created.Version, created.Draft with { Notes = "Nowa rewizja" }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        using var stale = await browser.SendAsync(HttpMethod.Post, path, new ApproveSalesInvoiceRequest(created.Version, created.Revision), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task SalesInvoiceApprovalAndEditAreAtomicWhicheverCommandCommitsFirst(bool approvalWins)
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, created) = await ApprovalScenarioAsync(f);
        using var delayedScope = f.Scope(); var provider = delayedScope.ServiceProvider;
        var gate = new ApprovalSaveGate(provider.GetRequiredService<ISalesInvoiceRepository>());
        var delayedService = ActivatorUtilities.CreateInstance<SalesInvoiceService>(provider, gate);
        var version = Convert.FromBase64String(created.Version);
        var delayed = approvalWins
            ? delayedService.UpdateAsync(tenant, created.Id, f.UserId, version, SalesInvoiceContractMapper.ToDomain(created.Draft with { Notes = "Edycja" }))
            : delayedService.ApproveAsync(tenant, created.Id, f.UserId, version, created.Revision);
        await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            using var winner = approvalWins
                ? await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/approve", new ApproveSalesInvoiceRequest(created.Version, created.Revision), tenantId: tenant)
                : await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(created.Version, created.Draft with { Notes = "Edycja" }), tenantId: tenant);
            Assert.True(winner.StatusCode == HttpStatusCode.OK, await winner.Content.ReadAsStringAsync());
        }
        finally { gate.Release.TrySetResult(); }
        await Assert.ThrowsAsync<SalesInvoiceConflictException>(() => delayed);
        using var check = f.Scope(); var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var actual = (await check.ServiceProvider.GetRequiredService<ISalesInvoiceService>().GetAsync(tenant, created.Id))!;
        Assert.Equal(approvalWins ? SalesInvoiceStatus.Approved : SalesInvoiceStatus.Draft, actual.Status);
        Assert.Equal(approvalWins ? created.Draft.Notes : "Edycja", actual.Notes);
        Assert.Equal(approvalWins ? 1 : 0, await db.Set<SalesInvoiceApproval>().CountAsync(x => x.TenantId == tenant));
        Assert.Equal(created.Revision + 1, actual.Revision); Assert.Equal(1, actual.Lines[0].Ordinal);
    }

    [Fact]
    public async Task SalesInvoiceConcurrentApprovalsPersistOnlyOneSnapshot()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, created) = await ApprovalScenarioAsync(f);
        using var delayedScope = f.Scope(); var provider = delayedScope.ServiceProvider;
        var gate = new ApprovalSaveGate(provider.GetRequiredService<ISalesInvoiceRepository>());
        var service = ActivatorUtilities.CreateInstance<SalesInvoiceService>(provider, gate);
        var delayed = service.ApproveAsync(tenant, created.Id, f.UserId, Convert.FromBase64String(created.Version), created.Revision);
        await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
        try
        {
            using var winner = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/approve", new ApproveSalesInvoiceRequest(created.Version, created.Revision), tenantId: tenant);
            Assert.True(winner.StatusCode == HttpStatusCode.OK, await winner.Content.ReadAsStringAsync());
        }
        finally { gate.Release.TrySetResult(); }
        await Assert.ThrowsAsync<SalesInvoiceConflictException>(() => delayed);
        using var check = f.Scope(); var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.Set<SalesInvoiceApproval>().CountAsync(x => x.TenantId == tenant));
        Assert.Equal(created.Revision + 1, (await db.SalesInvoices.SingleAsync(x => x.TenantId == tenant)).Revision);
    }

    [Fact]
    public async Task SalesInvoiceApprovalFailureAfterSqlWritesRollsBackStatusAndSnapshotTogether()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, _, created) = await ApprovalScenarioAsync(f);
        using var scope = f.Scope(); var provider = scope.ServiceProvider;
        var connection = provider.GetRequiredService<AppDbContext>().Database.GetConnectionString();
        await using var failingDb = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).AddInterceptors(new ApprovalCommitFailure()).Options);
        var service = ActivatorUtilities.CreateInstance<SalesInvoiceService>(provider, new SalesInvoiceRepository(failingDb));
        await Assert.ThrowsAsync<IOException>(() => service.ApproveAsync(tenant, created.Id, f.UserId, Convert.FromBase64String(created.Version), created.Revision));
        using var check = f.Scope(); var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var actual = await db.SalesInvoices.AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.Id == created.Id);
        Assert.Equal(SalesInvoiceStatus.Draft, actual.Status); Assert.Equal(created.Revision, actual.Revision);
        Assert.Equal(created.Version, Convert.ToBase64String(actual.RowVersion));
        Assert.False(await db.Set<SalesInvoiceApproval>().AnyAsync(x => x.TenantId == tenant));
    }

    private sealed class ApprovalCommitFailure : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
            => throw new IOException("Symulowana awaria po zapisie SQL, przed zatwierdzeniem transakcji.");
    }

    private sealed class ApprovalSaveGate(ISalesInvoiceRepository repository) : ISalesInvoiceRepository
    {
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<SalesInvoice> UnapproveAsync(Guid tenantId, Guid id, byte[] expectedVersion, int expectedRevision, string actor, DateTimeOffset now, CancellationToken cancellationToken) => repository.UnapproveAsync(tenantId, id, expectedVersion, expectedRevision, actor, now, cancellationToken);
        public Task<SalesInvoice?> GetAsync(Guid tenantId, Guid id, bool tracking, CancellationToken cancellationToken) => repository.GetAsync(tenantId, id, tracking, cancellationToken);
        public async Task SaveAsync(SalesInvoice invoice, byte[] expectedVersion, CancellationToken cancellationToken)
        { Arrived.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken); await repository.SaveAsync(invoice, expectedVersion, cancellationToken); }
        public Task AddAsync(SalesInvoice invoice, CancellationToken cancellationToken) => repository.AddAsync(invoice, cancellationToken);
        public Task<SalesInvoiceListPage> ListDraftsAsync(Guid tenantId, int page, int pageSize, CancellationToken cancellationToken) => repository.ListDraftsAsync(tenantId, page, pageSize, cancellationToken);
    }
}
