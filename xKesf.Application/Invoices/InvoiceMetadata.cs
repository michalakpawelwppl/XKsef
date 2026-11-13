namespace xKsef.Application.Invoices;

public sealed class InvoiceMetadata
{
    public required string InvoiceNumber { get; init; }

    public required DateOnly IssueDate { get; init; }

    // Amounts in the invoice currency; null means the XML does not provide a reliable total.
    public decimal? NetAmount { get; init; }

    public decimal? VatAmount { get; init; }

    public string? BuyerNip { get; init; }

    public string? BuyerName { get; init; }

    public required decimal GrossAmount { get; init; }

    public required string Currency { get; init; }

    public required string SellerName { get; init; }

    public required string SellerNip { get; init; }
}