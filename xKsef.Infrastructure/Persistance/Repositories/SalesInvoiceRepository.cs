using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
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
                .Include(x => x.ExchangeRates).Include(x => x.References).Include(x => x.Approvals).Include(x => x.Participants).AsSplitQuery().SingleOrDefaultAsync(cancellationToken);
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

    public async Task<SalesInvoice> UnapproveAsync(Guid tenantId, Guid id, byte[] expectedVersion, int expectedRevision, string actor, DateTimeOffset now, CancellationToken cancellationToken)
    {
        Guard(tenantId);
        if (context.ChangeTracker.HasChanges()) throw new InvalidOperationException("Cofnięcie zatwierdzenia wymaga jednostki pracy bez niezapisanych zmian.");
        await using var transaction = await context.Database.BeginTransactionAsync(cancellationToken);
        // Same SQL lock and order as numbering: reservation and unapproval cannot pass each other.
        var tenant = await context.Tenants.FromSqlInterpolated($"SELECT * FROM [Tenants] WITH (UPDLOCK,HOLDLOCK) WHERE [Id] = {tenantId}").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
        if (tenant is null || !tenant.IsActive) throw new KeyNotFoundException();
        context.ChangeTracker.Clear();
        var invoice = await GetAsync(tenantId, id, true, cancellationToken) ?? throw new KeyNotFoundException();
        if (expectedVersion is null || expectedVersion.Length != 8 || !invoice.RowVersion.SequenceEqual(expectedVersion)
            || await context.Set<xKsef.Domain.InvoiceNumberSeries.InvoiceNumberReservation>().AnyAsync(x => x.TenantId == tenantId && x.InvoiceId == id, cancellationToken))
            throw new SalesInvoiceConflictException();
        invoice.Unapprove(expectedRevision, actor, now);
        context.Entry(invoice).Property(x => x.RowVersion).OriginalValue = expectedVersion;
        try { await context.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); }
        catch (DbUpdateConcurrencyException) { throw new SalesInvoiceConflictException(); }
        return invoice;
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
            if (invoice.Status == SalesInvoiceStatus.Draft)
            {
                await context.Set<SalesInvoiceParticipant>().Where(x => x.TenantId == invoice.TenantId && x.SalesInvoiceId == invoice.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Ordinal, x => x.Ordinal + 100), cancellationToken);
                foreach (var entry in context.ChangeTracker.Entries<SalesInvoiceParticipant>().Where(x => x.Entity.TenantId == invoice.TenantId && x.Entity.SalesInvoiceId == invoice.Id && x.State != EntityState.Added))
                {
                    var ordinal = entry.Property(x => x.Ordinal); ordinal.OriginalValue += 100;
                    if (entry.State != EntityState.Deleted) ordinal.IsModified = true;
                }
                await context.Set<SalesInvoiceLine>().Where(x => x.TenantId == invoice.TenantId && x.SalesInvoiceId == invoice.Id)
                    .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Ordinal, x => x.Ordinal + 10000), cancellationToken);
                foreach (var entry in context.ChangeTracker.Entries<SalesInvoiceLine>().Where(x => x.Entity.TenantId == invoice.TenantId && x.Entity.SalesInvoiceId == invoice.Id && x.State != EntityState.Added))
                {
                    var ordinal = entry.Property(x => x.Ordinal);
                    ordinal.OriginalValue += 10000;
                    if (entry.State != EntityState.Deleted) ordinal.IsModified = true;
                }
            }
            await context.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch (DbUpdateConcurrencyException) { throw new SalesInvoiceConflictException(); }
        catch (DbUpdateException exception) when (invoice.Status == SalesInvoiceStatus.Approved
            && exception.InnerException is SqlException { Number: 2601 or 2627 } sql
            && sql.Message.Contains("IX_SalesInvoiceApprovals_TenantId_SalesInvoiceId", StringComparison.Ordinal))
        {
            // EF may insert the dependent history row before checking the header version.
            // A competing approval of the same revision is the same domain conflict in either order.
            throw new SalesInvoiceConflictException();
        }
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
