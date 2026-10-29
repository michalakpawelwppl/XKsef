using xKsef.Application.Exceptions;
using xKsef.Domain.Invoices;

namespace xKsef.Application.Invoices;

public sealed class InvoiceImportService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IInvoiceMetadataParser _metadataParser;
    

    public InvoiceImportService(
        IInvoiceRepository invoiceRepository,
        IInvoiceMetadataParser metadataParser)
    {
        _invoiceRepository = invoiceRepository;
        _metadataParser = metadataParser;
        
    }

    public async Task ImportAsync(
    ValidatedInvoice invoice,
    InvoiceDirection direction,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (await _invoiceRepository.ExistsAsync(
    invoice.KsefNumber,
    cancellationToken))
        {
            throw new DuplicateInvoiceException(invoice.KsefNumber);
        }
        InvoiceMetadata metadata = _metadataParser.Parse(
            invoice.GetXmlBytes());

        StoredInvoice storedInvoice = StoredInvoiceFactory.Create(
            invoice,
            metadata,
            direction);

        await _invoiceRepository.SaveAsync(
            storedInvoice,
            cancellationToken);
    }


    public async Task<InvoiceImportResult> ImportManyAsync(
        IReadOnlyList<ValidatedInvoice> invoices,
        InvoiceDirection direction,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoices);

        int importedCount = 0;
        int skippedCount = 0;

        foreach (var invoice in invoices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ImportAsync(invoice, direction, cancellationToken);
                importedCount++;
            }
            catch (DuplicateInvoiceException)
            {
                skippedCount++;
            }
        }

        return new InvoiceImportResult(
            importedCount,
            skippedCount);
    }
}