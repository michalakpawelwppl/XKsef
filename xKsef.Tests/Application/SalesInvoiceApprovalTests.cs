using System.Text.Json;
using xKsef.Application.SalesInvoices;
using xKsef.Application.Tenants;
using xKsef.Application.InvoiceNumbering;
using xKsef.Domain.InvoiceNumberSeries;
using NumberSeries = xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries;
using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;
using xKsef.Application.SalesInvoices.Snapshots;

namespace xKsef.Tests.Application;

public sealed class SalesInvoiceApprovalTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 27, 12, 0, 0, TimeSpan.Zero);
    private static SalesInvoice Invoice(bool complete = true)
    {
        var invoice = SalesInvoice.Create(Guid.NewGuid(), complete ? new()
        {
            PlannedIssueDate = new(2026, 11, 27),
            SaleDateMode = SaleDateMode.SameAsIssueDate, Fa3 = new() { Qualifications = SalesInvoiceSnapshotTestData.ConfirmedQualifications() },
            Seller = new() { Name = "Sprzedawca", Identifier = "8567346215", Address = new("PL", "Adres") },
            Buyer = new() { Name = "Konsument", IdentifierKind = TaxIdentifierKind.None },
            Lines = [new() { Id = Guid.NewGuid(), Description = "Towar", Unit = "szt.", Quantity = 1, UnitPrice = 100 }]
        } : new(), "creator", Now.AddMinutes(-5), new());
        typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.RowVersion))!.SetValue(invoice, new byte[8]);
        typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.NumberSeriesId))!.SetValue(invoice, Guid.NewGuid());
        return invoice;
    }
    private static SalesInvoiceService Service(Repository repository, bool allowed = true) => new(repository, null!, null!, new(), new Clock(), new(new()), new Permissions(allowed), new SeriesRepository(), new xKsef.Application.SalesInvoices.Snapshots.SalesInvoiceApprovalSnapshotWriter());

    [Fact]
    public async Task ApprovePreservesValidatedSourceAndRecordsActorTimeAndBothRevisions()
    {
        var invoice = Invoice(); var repository = new Repository(invoice); var service = Service(repository);
        var source = JsonSerializer.Serialize(invoice.ToDraft()); var revision = invoice.Revision;
        var approved = await service.ApproveAsync(invoice.TenantId, invoice.Id, "approver", invoice.RowVersion, revision);
        Assert.Same(invoice, approved); Assert.Equal(SalesInvoiceStatus.Approved, approved.Status); Assert.Equal(revision + 1, approved.Revision);
        var history = Assert.Single(approved.Approvals);
        Assert.Equal(source, JsonSerializer.Serialize(invoice.ToDraft()));
        Assert.Equal(revision, history.SourceRevision); Assert.Equal(approved.Revision, history.ApprovedRevision);
        Assert.Equal("approver", history.ApprovedBy); Assert.Equal(Now, history.ApprovedAt);
        Assert.Equal("approver", approved.UpdatedBy); Assert.Equal(Now, approved.UpdatedAt); Assert.Equal(1, repository.Saves);
        var snapshot = new SalesInvoiceApprovalSnapshotReader().Read(history);
        Assert.Equal(revision, snapshot.SourceRevision); Assert.Equal(0, snapshot.Metadata!.SourceStatus); Assert.Equal(invoice.GrossAmount, snapshot.Calculations!.Totals!.Gross!.Value.Value);
        Assert.Throws<SalesInvoiceConflictException>(() => approved.UpdateDraft(approved.ToDraft(), "editor", Now, new()));
        await Assert.ThrowsAsync<SalesInvoiceConflictException>(() => service.ApproveAsync(invoice.TenantId, invoice.Id, "approver", invoice.RowVersion, invoice.Revision));
        Assert.Single(invoice.Approvals); Assert.Equal(1, repository.Saves);
    }

    [Fact]
    public async Task FullValidationRejectsIncompleteDocumentWithoutMutationOrSave()
    {
        var invoice = Invoice(false); var repository = new Repository(invoice); var before = JsonSerializer.Serialize(invoice);
        var error = await Assert.ThrowsAsync<SalesInvoiceValidationException>(() => Service(repository).ApproveAsync(invoice.TenantId, invoice.Id, "user", invoice.RowVersion, invoice.Revision));
        Assert.True(error.Issues.Count > 1); Assert.Equal(before, JsonSerializer.Serialize(invoice)); Assert.Equal(0, repository.Saves);
    }

    [Fact]
    public async Task MissingPermissionIsRejectedInApplicationServiceBeforeReadingInvoice()
    {
        var invoice = Invoice(); var repository = new Repository(invoice);
        var error = await Assert.ThrowsAsync<TenantPermissionDeniedException>(() => Service(repository, false).ApproveAsync(invoice.TenantId, invoice.Id, "user", invoice.RowVersion, invoice.Revision));
        Assert.Equal(TenantPermissions.SalesInvoicesIssue, error.Permission); Assert.Equal(0, repository.Reads); Assert.Equal(0, repository.Saves);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StaleRowVersionOrRevisionCannotBeApproved(bool staleVersion)
    {
        var invoice = Invoice(); var repository = new Repository(invoice);
        await Assert.ThrowsAsync<SalesInvoiceConflictException>(() => Service(repository).ApproveAsync(invoice.TenantId, invoice.Id, "user", staleVersion ? new byte[] { 1, 0, 0, 0, 0, 0, 0, 0 } : invoice.RowVersion, staleVersion ? invoice.Revision : invoice.Revision - 1));
        Assert.Equal(SalesInvoiceStatus.Draft, invoice.Status); Assert.Empty(invoice.Approvals); Assert.Equal(0, repository.Saves);
    }

    [Fact]
    public async Task TenantBoundaryIsPreservedByApprovalLookup()
    {
        var invoice = Invoice(); var repository = new Repository(invoice);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service(repository).ApproveAsync(Guid.NewGuid(), invoice.Id, "user", invoice.RowVersion, invoice.Revision));
        Assert.Equal(0, repository.Saves);
    }

    [Theory]
    [InlineData(SalesInvoiceStatus.Approved)]
    [InlineData(SalesInvoiceStatus.Processing)]
    [InlineData(SalesInvoiceStatus.Issued)]
    [InlineData(SalesInvoiceStatus.Rejected)]
    [InlineData(SalesInvoiceStatus.Cancelled)]
    public void DomainTransitionOnlyAllowsDraft(SalesInvoiceStatus status)
    {
        var invoice = Invoice(); typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.Status))!.SetValue(invoice, status);
        Assert.Throws<SalesInvoiceConflictException>(() => invoice.Approve(invoice.Revision, "user", Now, "{}")); Assert.Empty(invoice.Approvals);
    }

    private sealed class Clock : TimeProvider { public override DateTimeOffset GetUtcNow() => Now; }
    private sealed class SeriesRepository : IInvoiceNumberSeriesRepository
    {
        public Task<NumberSeries?> GetAsync(Guid tenantId, Guid id, bool tracking, CancellationToken cancellationToken)
        {
            var series = NumberSeries.Create(tenantId, new("FV", "FV", "{prefix}/{number}/{year}", 6, NumberSeriesResetMode.Yearly), "user", Now);
            typeof(NumberSeries).GetProperty(nameof(NumberSeries.Id))!.SetValue(series, id); return Task.FromResult<NumberSeries?>(series);
        }
        public Task<T> ExecuteAsync<T>(Guid tenantId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<IReadOnlyList<NumberSeries>> ListAsync(Guid tenantId, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<bool> NameExistsAsync(Guid tenantId, Guid excludedId, string normalizedName, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<bool> HasCountersAsync(Guid tenantId, Guid seriesId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<IReadOnlyList<NumberSeriesConfiguration>> OtherConfigurationsAsync(Guid tenantId, Guid excludedId, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<InvoiceNumberSeriesDefault?> GetDefaultAsync(Guid tenantId, SalesInvoiceKind kind, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public void Add(NumberSeries series) => throw new InvalidOperationException();
        public void AddDefault(InvoiceNumberSeriesDefault assignment) => throw new InvalidOperationException();
        public void RemoveDefaults(IReadOnlyList<InvoiceNumberSeriesDefault> assignments) => throw new InvalidOperationException();
    }
    private sealed class Permissions(bool allowed) : ITenantPermissionService
    {
        public Task<bool> HasPermissionAsync(string userId, Guid tenantId, string permission, CancellationToken cancellationToken = default)
        { Assert.Equal(TenantPermissions.SalesInvoicesIssue, permission); return Task.FromResult(allowed); }
    }
    private sealed class Repository(SalesInvoice invoice) : ISalesInvoiceRepository
    {
        public Task<SalesInvoice> UnapproveAsync(Guid tenantId, Guid id, byte[] expectedVersion, int expectedRevision, string actor, DateTimeOffset now, CancellationToken cancellationToken) => throw new NotSupportedException();
        public int Saves { get; private set; }
        public int Reads { get; private set; }
        public Task<SalesInvoice?> GetAsync(Guid tenantId, Guid id, bool tracking, CancellationToken cancellationToken)
        { Reads++; Assert.True(tracking); return Task.FromResult<SalesInvoice?>(tenantId == invoice.TenantId && id == invoice.Id ? invoice : null); }
        public Task SaveAsync(SalesInvoice value, byte[] expectedVersion, CancellationToken cancellationToken) { Saves++; return Task.CompletedTask; }
        public Task AddAsync(SalesInvoice value, CancellationToken cancellationToken) => throw new InvalidOperationException();
        public Task<SalesInvoiceListPage> ListDraftsAsync(Guid tenantId, int page, int pageSize, CancellationToken cancellationToken) => throw new InvalidOperationException();
    }
}
