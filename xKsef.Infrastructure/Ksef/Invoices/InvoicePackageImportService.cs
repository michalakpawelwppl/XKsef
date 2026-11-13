using xKsef.Application.Invoices;
using xKsef.Domain.Invoices;


namespace xKsef.Infrastructure.Invoices;

internal sealed class InvoicePackageImportService : IInvoicePackageImportService
{
    private readonly InvoiceImportService _invoiceImportService;

    public InvoicePackageImportService(InvoiceImportService invoiceImportService)
    {
        _invoiceImportService = invoiceImportService;
    }

    public async Task<InvoiceImportResult> ImportFromDirectoryAsync(Guid tenantId, string directoryPath, InvoiceDirection direction, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));

        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        var metadataPath = Path.Combine(directoryPath, "_metadata.json");

        var invoices = await InvoicePackageMetadataReader.ReadFromFileAsync(metadataPath, cancellationToken);

        return await ImportValidatedInvoicesAsync(tenantId, directoryPath, invoices, direction, cancellationToken);
    }

    private async Task<InvoiceImportResult> ImportValidatedInvoicesAsync(Guid tenantId, string directoryPath, IReadOnlyList<ExportedInvoiceMetadata> invoices, InvoiceDirection direction, CancellationToken cancellationToken)
    {
        var validatedInvoices = await InvoicePackageValidator.ValidateAllInvoicesAsync(directoryPath, invoices, cancellationToken);

        return await _invoiceImportService.ImportManyAsync(tenantId, validatedInvoices, direction, cancellationToken);
    }
}