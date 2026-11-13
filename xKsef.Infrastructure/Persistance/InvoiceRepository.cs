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

    public async Task SaveAsync(StoredInvoice invoice, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        await _dbContext.Invoices.AddAsync(invoice, cancellationToken);

        try
        {
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsDuplicateKeyException(ex))
        {
            _dbContext.Entry(invoice).State = EntityState.Detached;

            throw new DuplicateInvoiceException(invoice.KsefNumber, ex);
        }
    }

    public async Task<bool> ExistsAsync(Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));

        return await _dbContext.Invoices.AnyAsync(x => x.TenantId == tenantId && x.KsefNumber == ksefNumber, cancellationToken);
    }

    public async Task<StoredInvoice?> GetByKsefNumberAsync(Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));

        return await _dbContext.Invoices.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.KsefNumber == ksefNumber, cancellationToken);
    }

    private static bool IsDuplicateKeyException(DbUpdateException exception)
    {
        return exception.InnerException is SqlException sqlException && sqlException.Number is 2601 or 2627;
    }

    public async Task<IReadOnlyList<StoredInvoice>> GetInvoicesAsync(Guid tenantId, DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));

        return await _dbContext.Invoices
            .AsNoTracking()
            .Where(x => x.TenantId == tenantId && x.IssueDate >= from && x.IssueDate <= to && x.Direction == direction)
            .OrderByDescending(x => x.IssueDate)
            .Select(x => new StoredInvoice
            {
                Id = x.Id,
                TenantId = x.TenantId,
                KsefNumber = x.KsefNumber,
                InvoiceNumber = x.InvoiceNumber,
                IssueDate = x.IssueDate,
                Direction = x.Direction,
                GrossAmount = x.GrossAmount,
                BuyerNip = x.BuyerNip,
                BuyerName = x.BuyerName,
                NetAmount = x.NetAmount,
                VatAmount = x.VatAmount,
                VatAmountPln = x.VatAmountPln,
                InvoicingDate = x.InvoicingDate,
                Currency = x.Currency,
                SellerName = x.SellerName,
                SellerNip = x.SellerNip,
                ImportedAt = x.ImportedAt
            })
            .ToListAsync(cancellationToken);
    }
}