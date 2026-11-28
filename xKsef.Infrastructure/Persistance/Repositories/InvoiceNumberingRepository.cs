using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using xKsef.Application.InvoiceNumbering;
using xKsef.Application.SalesInvoices;
using xKsef.Application.Tenants;
using xKsef.Domain.InvoiceNumberSeries;
using xKsef.Domain.Invoices;
using xKsef.Domain.SalesInvoices;
using NumberSeries = xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries;

namespace xKsef.Infrastructure.Persistence.Repositories;

public sealed class InvoiceNumberingRepository(AppDbContext db, ISalesInvoiceRepository invoices) : IInvoiceNumberingRepository
{
    public async Task<T> ExecuteAsync<T>(Guid tenant, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken)
    {
        if (tenant == Guid.Empty) throw new ArgumentException("Nieprawidłowy tenant.");
        if (db.ChangeTracker.HasChanges()) throw new InvalidOperationException("Operacja numerowania wymaga niezależnej jednostki pracy bez niezapisanych zmian.");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            var entity = await db.Tenants.FromSqlInterpolated($"SELECT * FROM [Tenants] WITH (UPDLOCK,HOLDLOCK) WHERE [Id] = {tenant}").AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (entity is null || !entity.IsActive) throw new TenantPermissionDeniedException(TenantPermissions.SalesInvoicesIssue);
            // A scoped worker may execute successive commands; never reuse stale tracked counters after waiting for the SQL lock.
            db.ChangeTracker.Clear();
            var result = await action(cancellationToken);
            await db.SaveChangesAsync(cancellationToken); await transaction.CommitAsync(cancellationToken); return result;
        }
        catch (DbUpdateConcurrencyException) { throw Conflict(); }
        catch (DbUpdateException exception) when (exception.InnerException is SqlException { Number: 2601 or 2627 or 1205 }) { throw Conflict(); }
        catch (SqlException exception) when (exception.Number == 1205) { throw Conflict(); }
        finally { db.ChangeTracker.Clear(); }
    }
    public Task<SalesInvoice?> GetInvoiceAsync(Guid tenant, Guid invoice, CancellationToken cancellationToken) => invoices.GetAsync(tenant, invoice, true, cancellationToken);
    public Task<NumberSeries?> GetSeriesAsync(Guid tenant, Guid series, CancellationToken cancellationToken) => db.Set<NumberSeries>().AsNoTracking().SingleOrDefaultAsync(x => x.TenantId == tenant && x.Id == series, cancellationToken);
    public Task<InvoiceNumberReservation?> GetOperationAsync(Guid tenant, Guid operation, CancellationToken cancellationToken) => db.Set<InvoiceNumberReservation>().SingleOrDefaultAsync(x => x.TenantId == tenant && x.OperationId == operation, cancellationToken);
    public async Task<IReadOnlyList<InvoiceNumberReservation>> GetReservationsAsync(Guid tenant, CancellationToken cancellationToken) => await db.Set<InvoiceNumberReservation>().AsNoTracking().Where(x => x.TenantId == tenant).ToArrayAsync(cancellationToken);
    public async Task<IReadOnlyList<string>> GetImportedSalesNumbersAsync(Guid tenant, CancellationToken cancellationToken) => await db.Invoices.AsNoTracking().Where(x => x.TenantId == tenant && x.Direction == InvoiceDirection.Sales).Select(x => x.InvoiceNumber).ToArrayAsync(cancellationToken);
    public Task<InvoiceNumberCounter?> GetCounterAsync(Guid tenant, Guid series, int period, CancellationToken cancellationToken) => db.Set<InvoiceNumberCounter>().SingleOrDefaultAsync(x => x.TenantId == tenant && x.SeriesId == series && x.Period == period, cancellationToken);
    public void AddCounter(InvoiceNumberCounter counter) => db.Set<InvoiceNumberCounter>().Add(counter);
    public void AddReservation(InvoiceNumberReservation reservation) => db.Set<InvoiceNumberReservation>().Add(reservation);
    private static InvoiceNumberingConflictException Conflict() => new("ConcurrencyConflict", "Konflikt numerowania. Ponów tę samą operację z tym samym identyfikatorem.");
}
