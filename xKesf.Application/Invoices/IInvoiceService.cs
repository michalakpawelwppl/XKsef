using xKsef.Application.Invoices;
using xKsef.Domain.Invoices;

public interface IInvoiceService
{
    Task<IReadOnlyList<Invoice>> GetInvoicesAsync(
        DateOnly from,
        DateOnly to,
        InvoiceDirection direction,
        CancellationToken cancellationToken = default);
}