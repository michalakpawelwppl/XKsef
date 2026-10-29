using xKsef.Application.Invoices;
using xKsef.Contracts.Invoices;
using xKsef.Domain.Invoices;

public interface IInvoiceApiClient
{
    Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default);

    Task<string> GetInvoiceXmlAsync(
        string ksefNumber,
        CancellationToken cancellationToken = default);

    Task<byte[]> GetInvoicePdfAsync(
    string ksefNumber,
    CancellationToken cancellationToken = default);

    Task<InvoicePackageReadResult> ImportFromKsefAsync(
    DateOnly from,
    DateOnly to,
    InvoiceDirection direction,
    CancellationToken cancellationToken = default);
}