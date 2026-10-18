using xKsef.Domain.Invoices;

namespace xKsef.Application.Invoices;

public static class StoredInvoiceFactory
{
    public static StoredInvoice Create(ValidatedInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        return new StoredInvoice
        {
            KsefNumber = invoice.KsefNumber,
            XmlBytes = invoice.GetXmlBytes(),
            ImportedAt = DateTimeOffset.UtcNow
        };
    }
}