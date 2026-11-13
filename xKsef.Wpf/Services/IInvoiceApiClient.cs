using xKsef.Contracts.Invoices;
using xKsef.Wpf.Http;

public interface IInvoiceApiClient
{
    Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(ApiRequestContext context, DateOnly from, DateOnly to, InvoiceDirection direction);

    Task<InvoiceQueryResultDto> QueryInvoicesAsync(ApiRequestContext context, DateOnly from, DateOnly to, InvoiceDirection direction, InvoiceDateKind dateKind, string? timeZoneId);

    Task<byte[]> GetInvoicePdfAsync(ApiRequestContext context, string ksefNumber);

    Task<InvoicePackageReadResult> ImportFromKsefAsync(ApiRequestContext context, DateOnly from, DateOnly to, InvoiceDirection direction);
}