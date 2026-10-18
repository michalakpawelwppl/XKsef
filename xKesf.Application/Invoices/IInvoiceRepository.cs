using xKsef.Domain.Invoices;

namespace xKsef.Application.Invoices;

public interface IInvoiceRepository
{
    Task SaveAsync(
        StoredInvoice invoice,
        CancellationToken cancellationToken = default);

    Task<bool> ExistsAsync(
    string ksefNumber,
    CancellationToken cancellationToken = default);
}