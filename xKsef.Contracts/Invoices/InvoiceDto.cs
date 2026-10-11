namespace xKsef.Contracts.Invoices;

public class InvoiceDto
{
    public string KsefNumber { get; set; } = string.Empty;
    public string InvoiceNumber { get; set; } = string.Empty;
    public string SellerName { get; set; } = string.Empty;
    public string SellerNip { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public decimal GrossAmount { get; set; }
    public string Currency { get; set; } = "PLN";
}