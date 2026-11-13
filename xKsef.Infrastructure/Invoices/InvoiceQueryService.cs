using Microsoft.EntityFrameworkCore;
using xKsef.Application.Invoices;
using xKsef.Contracts.Invoices;
using xKsef.Infrastructure.Persistence;
using DomainDirection = xKsef.Domain.Invoices.InvoiceDirection;

namespace xKsef.Infrastructure.Invoices;

public sealed class InvoiceQueryService(AppDbContext dbContext) : IInvoiceQueryService
{
    public async Task<InvoiceQueryResultDto> QueryAsync(Guid tenantId, InvoiceQueryFilter filter, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        ArgumentNullException.ThrowIfNull(filter);
        var query = dbContext.Invoices.AsNoTracking().Where(invoice => invoice.TenantId == tenantId && invoice.Direction == (DomainDirection)filter.Direction);
        if (filter.DateKind == InvoiceDateKind.IssueDate)
            query = query.Where(invoice => invoice.IssueDate >= filter.From && invoice.IssueDate <= filter.To);
        else
            query = query.Where(invoice => invoice.InvoicingDate.HasValue && invoice.InvoicingDate >= filter.FromUtc && invoice.InvoicingDate < filter.ToUtcExclusive);

        var ordered = filter.DateKind == InvoiceDateKind.IssueDate
            ? query.OrderByDescending(invoice => invoice.IssueDate).ThenByDescending(invoice => invoice.Id)
            : query.OrderByDescending(invoice => invoice.InvoicingDate).ThenByDescending(invoice => invoice.Id);
        var items = await ordered.Select(invoice => new InvoiceDto
        {
            KsefNumber = invoice.KsefNumber,
            InvoiceNumber = invoice.InvoiceNumber,
            SellerName = invoice.SellerName,
            SellerNip = invoice.SellerNip,
            BuyerNip = invoice.BuyerNip,
            BuyerName = invoice.BuyerName,
            IssueDate = invoice.IssueDate,
            InvoicingDate = invoice.InvoicingDate,
            NetAmount = invoice.NetAmount,
            VatAmount = invoice.VatAmount,
            VatAmountPln = invoice.VatAmountPln,
            GrossAmount = invoice.GrossAmount,
            Currency = invoice.Currency
        }).ToListAsync(cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        return new InvoiceQueryResultDto(items, Summarize(items));
    }

    internal static IReadOnlyList<InvoiceCurrencySummaryDto> Summarize(IReadOnlyList<InvoiceDto> items)
    {
        return items.GroupBy(invoice => invoice.Currency, StringComparer.Ordinal).OrderBy(group => group.Key, StringComparer.Ordinal).Select(group =>
        {
            var invoices = group.ToArray();
            int missingNet = invoices.Count(invoice => !invoice.NetAmount.HasValue);
            int missingVat = invoices.Count(invoice => !invoice.VatAmount.HasValue);
            return new InvoiceCurrencySummaryDto(group.Key, invoices.Length,
                missingNet == 0 ? invoices.Sum(invoice => invoice.NetAmount!.Value) : null,
                missingVat == 0 ? invoices.Sum(invoice => invoice.VatAmount!.Value) : null,
                invoices.Sum(invoice => invoice.GrossAmount), missingNet, missingVat);
        }).ToList();
    }
}
