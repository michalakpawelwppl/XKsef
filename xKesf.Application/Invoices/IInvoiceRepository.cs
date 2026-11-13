using xKsef.Domain.Invoices;

namespace xKsef.Application.Invoices;

public interface IInvoiceRepository
{
    Task SaveAsync(StoredInvoice invoice, CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default);

    Task<StoredInvoice?> GetByKsefNumberAsync(Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<StoredInvoice>> GetInvoicesAsync(Guid tenantId, DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default);
}