namespace xKsef.Application.Invoices;

public interface IInvoiceRepository
{
    Task SaveXmlAsync(
        string xml,
        CancellationToken cancellationToken = default);
}