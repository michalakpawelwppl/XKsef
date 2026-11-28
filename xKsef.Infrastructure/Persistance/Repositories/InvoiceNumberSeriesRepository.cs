using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using xKsef.Application.InvoiceNumbering;
using xKsef.Application.Tenants;
using xKsef.Domain.InvoiceNumberSeries;
using xKsef.Domain.SalesInvoices;
using NumberSeries = xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries;

namespace xKsef.Infrastructure.Persistence.Repositories;

public sealed class InvoiceNumberSeriesRepository(AppDbContext db) : IInvoiceNumberSeriesRepository
{
    public async Task<T> ExecuteAsync<T>(Guid tenantId, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        Guard(tenantId);
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            // Same lock as TenantUsersService: serialize namespace/default checks and authorization after waiting.
            var tenant = await db.Tenants.FromSqlInterpolated($"SELECT * FROM [Tenants] WITH (UPDLOCK,HOLDLOCK) WHERE [Id] = {tenantId}").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (tenant is null || !tenant.IsActive) throw new TenantPermissionDeniedException(TenantPermissions.InvoiceNumberSeriesManage);
            var result = await action(cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException) { throw new InvoiceNumberSeriesConflictException(); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 or 1205 }) { throw new InvoiceNumberSeriesConflictException(); }
        catch (SqlException exception) when (exception.Number == 1205) { throw new InvoiceNumberSeriesConflictException(); }
    }

    public async Task<NumberSeries?> GetAsync(Guid tenantId, Guid id, bool tracking, CancellationToken cancellationToken)
    {
        Guard(tenantId);
        var query = db.Set<NumberSeries>().Where(x => x.TenantId == tenantId && x.Id == id);
        if (tracking) return await query.Include(x => x.History).Include(x => x.Defaults).AsSingleQuery().SingleOrDefaultAsync(cancellationToken);
        for (var attempt = 0; attempt < 3; attempt++)
        {
            var series = await query.AsNoTracking().Include(x => x.History).Include(x => x.Defaults).AsSplitQuery().SingleOrDefaultAsync(cancellationToken);
            if (series is null) return null;
            var version = await query.AsNoTracking().Select(x => x.RowVersion).SingleOrDefaultAsync(cancellationToken);
            if (version is null) return null;
            if (series.RowVersion.SequenceEqual(version)) return series;
        }
        throw new InvoiceNumberSeriesConflictException();
    }
    public async Task<IReadOnlyList<NumberSeries>> ListAsync(Guid tenantId, CancellationToken cancellationToken)
    {
        Guard(tenantId);
        return await db.Set<NumberSeries>().AsNoTracking().Where(x => x.TenantId == tenantId).Include(x => x.History).Include(x => x.Defaults).AsSingleQuery().OrderBy(x => x.NormalizedName).ThenBy(x => x.Id).ToArrayAsync(cancellationToken);
    }
    public Task<bool> NameExistsAsync(Guid tenantId, Guid excludedId, string normalizedName, CancellationToken cancellationToken)
    {
        Guard(tenantId);
        return db.Set<NumberSeries>().AnyAsync(x => x.TenantId == tenantId && x.Id != excludedId && x.NormalizedName == normalizedName, cancellationToken);
    }
    public Task<bool> HasCountersAsync(Guid tenantId, Guid seriesId, CancellationToken cancellationToken)
    {
        Guard(tenantId);
        return db.Set<InvoiceNumberCounter>().AnyAsync(x => x.TenantId == tenantId && x.SeriesId == seriesId, cancellationToken);
    }
    public async Task<IReadOnlyList<NumberSeriesConfiguration>> OtherConfigurationsAsync(Guid tenantId, Guid excludedId, CancellationToken cancellationToken)
    {
        Guard(tenantId);
        return (await db.Set<InvoiceNumberSeriesRevision>().AsNoTracking().Where(x => x.TenantId == tenantId && x.SeriesId != excludedId).Select(x => x.Configuration).ToArrayAsync(cancellationToken)).Distinct().ToArray();
    }
    public Task<InvoiceNumberSeriesDefault?> GetDefaultAsync(Guid tenantId, SalesInvoiceKind kind, CancellationToken cancellationToken)
    {
        Guard(tenantId);
        return db.Set<InvoiceNumberSeriesDefault>().SingleOrDefaultAsync(x => x.TenantId == tenantId && x.InvoiceKind == kind, cancellationToken);
    }
    public void Add(NumberSeries series) { Guard(series.TenantId); db.Set<NumberSeries>().Add(series); }
    public void AddDefault(InvoiceNumberSeriesDefault assignment) { Guard(assignment.TenantId); db.Set<InvoiceNumberSeriesDefault>().Add(assignment); }
    public void RemoveDefaults(IReadOnlyList<InvoiceNumberSeriesDefault> assignments) => db.Set<InvoiceNumberSeriesDefault>().RemoveRange(assignments);
    private static void Guard(Guid tenantId) { if (tenantId == Guid.Empty) throw new ArgumentException("Nieprawidłowy tenant."); }
}
