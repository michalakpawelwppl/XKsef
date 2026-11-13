using xKsef.Domain.Invoices;

namespace xKsef.Application.Invoices;

public interface IInvoicePackageImportService
{
    Task<InvoiceImportResult> ImportFromDirectoryAsync(Guid tenantId, string directoryPath, InvoiceDirection direction, CancellationToken cancellationToken = default);
}