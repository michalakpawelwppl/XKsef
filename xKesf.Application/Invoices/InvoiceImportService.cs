using xKsef.Application.Exceptions;
using xKsef.Domain.Invoices;

namespace xKsef.Application.Invoices;

public sealed class InvoiceImportService
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IInvoiceMetadataParser _metadataParser;
    

    public InvoiceImportService(IInvoiceRepository invoiceRepository, IInvoiceMetadataParser metadataParser)
    {
        _invoiceRepository = invoiceRepository;
        _metadataParser = metadataParser;
    }

    public async Task ImportAsync(Guid tenantId, ValidatedInvoice invoice, InvoiceDirection direction, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));

        ArgumentNullException.ThrowIfNull(invoice);

        if (await _invoiceRepository.ExistsAsync(tenantId, invoice.KsefNumber, cancellationToken))
            throw new DuplicateInvoiceException(invoice.KsefNumber);

        InvoiceMetadata metadata = _metadataParser.Parse(invoice.GetXmlBytes());

        StoredInvoice storedInvoice = StoredInvoiceFactory.Create(invoice, metadata, direction);

        storedInvoice.TenantId = tenantId;

        await _invoiceRepository.SaveAsync(storedInvoice, cancellationToken);
    }


    public async Task<InvoiceImportResult> ImportManyAsync(Guid tenantId, IReadOnlyList<ValidatedInvoice> invoices, InvoiceDirection direction, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));

        ArgumentNullException.ThrowIfNull(invoices);

        int importedCount = 0;
        int skippedCount = 0;

        foreach (var invoice in invoices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            try
            {
                await ImportAsync(tenantId, invoice, direction, cancellationToken);
                importedCount++;
            }
            catch (DuplicateInvoiceException)
            {
                skippedCount++;
            }
        }

        return new InvoiceImportResult(importedCount, skippedCount);
    }
}