using InvoicePackageReadResult = xKsef.Contracts.Invoices.InvoicePackageReadResult;
using xKsef.Domain.Invoices;

public interface IInvoiceService
{
    Task<IReadOnlyList<Invoice>> GetInvoicesAsync(Guid tenantId, DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default);

    Task<string> GetInvoiceXmlAsync(string userId, Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default);

    Task<InvoicePackageReadResult> ImportInvoicesAsync(string userId, Guid tenantId, DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default);

}