using xKsef.Domain.InvoiceNumberSeries;
using xKsef.Domain.SalesInvoices;
using NumberSeries = xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries;

namespace xKsef.Application.InvoiceNumbering;

public interface IInvoiceNumberSeriesRepository
{
    Task<T> ExecuteAsync<T>(Guid tenantId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
    Task<NumberSeries?> GetAsync(Guid tenantId, Guid id, bool tracking, CancellationToken cancellationToken);
    Task<IReadOnlyList<NumberSeries>> ListAsync(Guid tenantId, CancellationToken cancellationToken);
    Task<bool> NameExistsAsync(Guid tenantId, Guid excludedId, string normalizedName, CancellationToken cancellationToken);
    Task<bool> HasCountersAsync(Guid tenantId, Guid seriesId, CancellationToken cancellationToken);
    Task<IReadOnlyList<NumberSeriesConfiguration>> OtherConfigurationsAsync(Guid tenantId, Guid excludedId, CancellationToken cancellationToken);
    Task<InvoiceNumberSeriesDefault?> GetDefaultAsync(Guid tenantId, SalesInvoiceKind kind, CancellationToken cancellationToken);
    void Add(NumberSeries series);
    void AddDefault(InvoiceNumberSeriesDefault assignment);
    void RemoveDefaults(IReadOnlyList<InvoiceNumberSeriesDefault> assignments);
}
