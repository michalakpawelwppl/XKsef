using xKsef.Application.Exceptions;

namespace xKsef.Application.Invoices;

public sealed class InvoiceImportService
{
    private readonly IInvoiceRepository _invoiceRepository;

    public InvoiceImportService(IInvoiceRepository invoiceRepository)
    {
        _invoiceRepository = invoiceRepository;
    }

    public async Task ImportAsync(
        ValidatedInvoice invoice,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var storedInvoice = StoredInvoiceFactory.Create(invoice);

        await _invoiceRepository.SaveAsync(
            storedInvoice,
            cancellationToken);
    }
    public async Task<InvoiceImportResult> ImportManyAsync(
    IReadOnlyList<ValidatedInvoice> invoices,
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
                await ImportAsync(invoice, cancellationToken);
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