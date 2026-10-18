namespace xKsef.Application.Invoices.Visualization;

public class InvoiceVisualizationModel
{
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }

    public string SellerName { get; set; } = string.Empty;
    public string SellerNip { get; set; } = string.Empty;

    public string BuyerName { get; set; } = string.Empty;
    public string BuyerNip { get; set; } = string.Empty;

    public decimal GrossAmount { get; set; }
    public string Currency { get; set; } = "PLN";
    public List<InvoiceItemVisualizationModel> Items { get; set; } = new();
}