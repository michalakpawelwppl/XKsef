using xKsef.Domain.Invoices;

namespace xKsef.Application.Invoices;

public static class StoredInvoiceFactory
{

    public static StoredInvoice Create(
ValidatedInvoice invoice,
InvoiceMetadata metadata,
InvoiceDirection direction)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(metadata);

        return new StoredInvoice
        {
            KsefNumber = invoice.KsefNumber,
            XmlBytes = invoice.GetXmlBytes(),
            ImportedAt = DateTimeOffset.UtcNow,

            InvoiceNumber = metadata.InvoiceNumber,
            IssueDate = metadata.IssueDate,
            GrossAmount = metadata.GrossAmount,
            BuyerNip = metadata.BuyerNip,
            BuyerName = metadata.BuyerName,
            NetAmount = metadata.NetAmount,
            VatAmount = metadata.VatAmount,
            VatAmountPln = invoice.VatAmountPln,
            InvoicingDate = invoice.InvoicingDate,
            Currency = metadata.Currency,
            SellerName = metadata.SellerName,
            SellerNip = metadata.SellerNip,
            Direction = direction
        };
    }
}
