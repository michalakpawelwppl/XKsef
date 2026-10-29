namespace xKsef.Application.Invoices.Pdf;

public interface IInvoicePdfGenerator
{
    Task<byte[]> GenerateAsync(
        byte[] invoiceXml,
        string ksefNumber,
        CancellationToken cancellationToken = default);
}