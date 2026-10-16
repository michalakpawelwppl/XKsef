using xKsef.Application.Invoices;
using xKsef.Domain.Invoices;

namespace xKsef.Infrastructure.Invoices;

public class FakeInvoiceService : IInvoiceService
{
    public Task<IReadOnlyList<Invoice>> GetInvoicesAsync(
        DateOnly from,
        DateOnly to,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        IReadOnlyList<Invoice> invoices = new List<Invoice>
        {
            new()
            {
                KsefNumber = "TEST-KSEF-001",
                InvoiceNumber = "FV/10/2026/001",
                SellerName = "ABC Sp. z o.o.",
                SellerNip = "1234567890",
                IssueDate = new DateOnly(2026, 10, 1),
                GrossAmount = 1230.00m
            },
            new()
            {
                KsefNumber = "TEST-KSEF-002",
                InvoiceNumber = "FV/10/2026/002",
                SellerName = "XYZ S.A.",
                SellerNip = "9876543210",
                IssueDate = new DateOnly(2026, 10, 5),
                GrossAmount = 4920.00m
            }
        };

        return Task.FromResult<IReadOnlyList<Invoice>>(
            invoices.Where(x =>
                x.IssueDate >= from &&
                x.IssueDate <= to).ToList());
    }

    public Task<IReadOnlyList<Invoice>> GetInvoicesAsync(DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }

    public Task<string> GetInvoiceXmlAsync(string ksefNumber, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}