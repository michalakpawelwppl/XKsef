using xKsef.Contracts.Invoices;

public interface IInvoiceApiClient
{
    Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(
        DateOnly from,
        DateOnly to,
        InvoiceDirection direction,
        CancellationToken cancellationToken = default);

    Task<string> GetInvoiceXmlAsync(
        string ksefNumber,
        CancellationToken cancellationToken = default);
}