namespace xKsef.Application.Invoices;

public sealed class InvoiceMetadata
{
    public required string InvoiceNumber { get; init; }

    public required DateOnly IssueDate { get; init; }

    public required decimal GrossAmount { get; init; }

    public required string Currency { get; init; }

    public required string SellerName { get; init; }

    public required string SellerNip { get; init; }
}