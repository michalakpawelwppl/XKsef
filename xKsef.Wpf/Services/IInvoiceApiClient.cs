using xKsef.Contracts.Invoices;

namespace xKsef.Wpf.Services;

public interface IInvoiceApiClient
{
    Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(
        DateOnly from,
        DateOnly to,
        InvoiceDirection direction,
        CancellationToken cancellationToken = default);
}