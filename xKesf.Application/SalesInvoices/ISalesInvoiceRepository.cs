using xKsef.Domain.SalesInvoices;

namespace xKsef.Application.SalesInvoices;

public sealed record SalesInvoiceListPage(IReadOnlyList<SalesInvoiceListItem> Items, int TotalCount, int Page, int PageSize);
public sealed record SalesInvoiceListItem(Guid Id, SalesInvoiceStatus Status, DateOnly? PlannedIssueDate, string Currency, decimal? NetAmount, decimal? VatAmount, decimal? GrossAmount, DateTimeOffset UpdatedAt, int Revision);

public interface ISalesInvoiceRepository
{
    Task<SalesInvoice?> GetAsync(Guid tenantId, Guid id, bool tracking, CancellationToken cancellationToken);
    Task AddAsync(SalesInvoice invoice, CancellationToken cancellationToken);
    Task SaveAsync(SalesInvoice invoice, byte[] expectedVersion, CancellationToken cancellationToken);
    Task<SalesInvoiceListPage> ListDraftsAsync(Guid tenantId, int page, int pageSize, CancellationToken cancellationToken);
}

public interface ISalesInvoiceService
{
    Task<SalesInvoice> CreateAsync(Guid tenantId, string userId, SalesInvoiceDraft draft, CancellationToken cancellationToken = default);
    Task<SalesInvoice?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default);
    Task<SalesInvoice> UpdateAsync(Guid tenantId, Guid id, string userId, byte[] expectedVersion, SalesInvoiceDraft draft, CancellationToken cancellationToken = default);
    Task<SalesInvoice> RecalculateAsync(Guid tenantId, Guid id, string userId, byte[] expectedVersion, CancellationToken cancellationToken = default);
    Task<SalesInvoiceListPage> ListDraftsAsync(Guid tenantId, int page, int pageSize, CancellationToken cancellationToken = default);
}
