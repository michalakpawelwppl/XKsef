namespace xKsef.Application.Invoices;

public interface IInvoiceImportTransaction
{
    Task ExecuteAsync(
        Func<CancellationToken, Task> operation,
        CancellationToken cancellationToken = default);
}