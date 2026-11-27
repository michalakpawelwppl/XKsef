using Microsoft.EntityFrameworkCore;
using xKsef.Application.SalesInvoices;
using xKsef.Domain.SalesInvoices;

namespace xKsef.Infrastructure.Persistence.Repositories;

public sealed class SalesInvoiceRepository(AppDbContext context) : ISalesInvoiceRepository
{
    public async Task<SalesInvoice?> GetAsync(Guid tenantId, Guid id, bool tracking, CancellationToken cancellationToken)
    {
        Guard(tenantId);
        if (tracking)
        {
            var local = context.SalesInvoices.Local.SingleOrDefault(x => x.TenantId == tenantId && x.Id == id);
            if (local is not null) return local;
        }
        var query = context.SalesInvoices.AsNoTracking().Where(x => x.TenantId == tenantId && x.Id == id);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var invoice = await query.Include(x => x.Lines).Include(x => x.Parties).Include(x => x.TaxSummaries).Include(x => x.Details)
                .Include(x => x.ExchangeRates).Include(x => x.References).AsSplitQuery().SingleOrDefaultAsync(cancellationToken);
            if (invoice is null) return null;
            // Split queries must not combine a header from one revision with children from another.
            var version = await query.Select(x => x.RowVersion).SingleOrDefaultAsync(cancellationToken);
            if (version is null) return null;
            if (!invoice.RowVersion.SequenceEqual(version)) continue;
            if (tracking) context.Attach(invoice);
            return invoice;
        }
        throw new SalesInvoiceConflictException();
    }

    public async Task AddAsync(SalesInvoice invoice, CancellationToken cancellationToken)
    {
        Guard(invoice.TenantId);
        context.SalesInvoices.Add(invoice);
        await context.SaveChangesAsync(cancellationToken);
    }

    public async Task SaveAsync(SalesInvoice invoice, byte[] expectedVersion, CancellationToken cancellationToken)
    {
        Guard(invoice.TenantId);
        context.Entry(invoice).Property(x => x.RowVersion).OriginalValue = expectedVersion;
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            context.ChangeTracker.DetectChanges();
            // Move persisted ordinals out of the final range before swapping/inserting rows.
            // Both this staging update and the aggregate's optimistic version check roll back together.
            await context.Set<SalesInvoiceLine>().Where(x => x.TenantId == invoice.TenantId && x.SalesInvoiceId == invoice.Id)
                .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Ordinal, x => x.Ordinal + 10000), cancellationToken);
            foreach (var entry in context.ChangeTracker.Entries<SalesInvoiceLine>().Where(x => x.Entity.TenantId == invoice.TenantId && x.Entity.SalesInvoiceId == invoice.Id && x.State != EntityState.Added))
            {
                var ordinal = entry.Property(x => x.Ordinal);
                ordinal.OriginalValue += 10000;
                if (entry.State != EntityState.Deleted) ordinal.IsModified = true;
            }
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { throw new SalesInvoiceConflictException(); }
    }

    public async Task<SalesInvoiceListPage> ListDraftsAsync(Guid tenantId, int page, int pageSize, CancellationToken cancellationToken)
    {
        Guard(tenantId);
        var query = context.SalesInvoices.AsNoTracking().Where(x => x.TenantId == tenantId && x.Status == SalesInvoiceStatus.Draft);
        var count = await query.CountAsync(cancellationToken);
        var items = await query.OrderByDescending(x => x.UpdatedAt).ThenBy(x => x.Id).Skip(checked((page - 1) * pageSize)).Take(pageSize)
            .Select(x => new SalesInvoiceListItem(x.Id, x.Status, x.PlannedIssueDate, x.Currency, x.NetAmount, x.VatAmount, x.GrossAmount, x.UpdatedAt, x.Revision)).ToListAsync(cancellationToken);
        return new(items, count, page, pageSize);
    }
    private static void Guard(Guid tenantId) { if (tenantId == Guid.Empty) throw new ArgumentException("Nieprawidłowy tenant."); }
}
