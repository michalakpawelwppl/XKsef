using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using xKsef.Application.Exceptions;
using xKsef.Application.Invoices;
using xKsef.Domain.Invoices;

namespace xKsef.Infrastructure.Persistence;

internal sealed class InvoiceRepository : IInvoiceRepository
{
    private readonly AppDbContext _dbContext;

    public InvoiceRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task SaveAsync(
    StoredInvoice invoice,
    CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        await _dbContext.Invoices.AddAsync(invoice, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex)
     when (IsDuplicateKeyException(ex))
        {
            _dbContext.Entry(invoice).State = EntityState.Detached;

            throw new DuplicateInvoiceException(
                invoice.KsefNumber,
                ex);
        }
    }

    public async Task<bool> ExistsAsync(
        string ksefNumber,
        CancellationToken cancellationToken = default)
    {
        return await _dbContext.Invoices
            .AnyAsync(
                x => x.KsefNumber == ksefNumber,
                cancellationToken);
    }

    private static bool IsDuplicateKeyException(
        DbUpdateException exception)
    {
        return exception.InnerException is SqlException sqlException
            && sqlException.Number == 2601;
    }
}