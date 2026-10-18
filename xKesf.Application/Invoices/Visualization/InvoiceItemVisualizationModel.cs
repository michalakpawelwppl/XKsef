namespace xKsef.Application.Invoices.Visualization;

public class InvoiceItemVisualizationModel
{
    public int LineNumber { get; set; }

    public string Description { get; set; } = string.Empty;

    public decimal? Quantity { get; set; }

    public string Unit { get; set; } = string.Empty;

    public decimal? NetUnitPrice { get; set; }

    public decimal? NetAmount { get; set; }

    public string VatRate { get; set; } = string.Empty;
}