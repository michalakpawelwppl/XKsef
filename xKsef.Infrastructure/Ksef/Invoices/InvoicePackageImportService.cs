using xKsef.Application.Invoices;
using xKsef.Domain.Invoices;


namespace xKsef.Infrastructure.Invoices;

internal sealed class InvoicePackageImportService : IInvoicePackageImportService
{
    private readonly InvoiceImportService _invoiceImportService;

    public InvoicePackageImportService(
        InvoiceImportService invoiceImportService)
    {
        _invoiceImportService = invoiceImportService;
    }

    public async Task<InvoiceImportResult> ImportFromDirectoryAsync(
        string directoryPath,
        InvoiceDirection direction,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directoryPath);

        var metadataPath = Path.Combine(
            directoryPath,
            "_metadata.json");

        var invoices = await InvoicePackageMetadataReader
            .ReadFromFileAsync(metadataPath, cancellationToken);

        return await ImportValidatedInvoicesAsync(
    directoryPath,
    invoices,
    direction,
    cancellationToken);
    }

    private async Task<InvoiceImportResult> ImportValidatedInvoicesAsync(
        string directoryPath,
        IReadOnlyList<ExportedInvoiceMetadata> invoices,
        InvoiceDirection direction,
        CancellationToken cancellationToken)
    {
        var validatedInvoices =
            await InvoicePackageValidator.ValidateAllInvoicesAsync(
                directoryPath,
                invoices,
                cancellationToken);

        return await _invoiceImportService.ImportManyAsync(
            validatedInvoices,
            direction,
            cancellationToken);
    }
}