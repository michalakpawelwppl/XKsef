namespace xKsef.Application.Invoices;

public interface IInvoiceMetadataParser
{
    InvoiceMetadata Parse(byte[] xmlBytes);
}