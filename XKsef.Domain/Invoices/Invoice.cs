namespace xKsef.Domain.Invoices;

public class Invoice
{
    public string KsefNumber { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public string SellerName { get; set; } = string.Empty;
    public string SellerNip { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public decimal GrossAmount { get; set; }
    public string Currency { get; set; } = "PLN";
    public string? BuyerNip { get; set; }
    public string? BuyerName { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? VatAmountPln { get; set; }
    public DateTimeOffset? InvoicingDate { get; set; }
}