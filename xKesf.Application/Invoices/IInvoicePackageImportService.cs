using xKsef.Domain.Invoices;

namespace xKsef.Application.Invoices;

public interface IInvoicePackageImportService
{
    Task<InvoiceImportResult> ImportFromDirectoryAsync(
        string directoryPath,
        InvoiceDirection direction,
        CancellationToken cancellationToken = default);
}