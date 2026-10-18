namespace xKsef.Application.Invoices;

public interface IInvoicePackageImportService
{
    Task<InvoiceImportResult> ImportFromDirectoryAsync(
        string directoryPath,
        CancellationToken cancellationToken = default);
}