using xKsef.Contracts.Invoices;

namespace xKsef.Application.Invoices;

public interface IInvoiceQueryService
{
    Task<InvoiceQueryResultDto> QueryAsync(Guid tenantId, InvoiceQueryFilter filter, CancellationToken cancellationToken = default);
}
