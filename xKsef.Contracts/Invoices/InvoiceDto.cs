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
    public string? BuyerNip { get; set; }
    public string? BuyerName { get; set; }
    public decimal? NetAmount { get; set; }
    public decimal? VatAmount { get; set; }
    public decimal? VatAmountPln { get; set; }
    public DateTimeOffset? InvoicingDate { get; set; }

    // Stored amounts are decimal(18,2); preserve the existing numeric JSON fields.
    public string GrossAmountExact => GrossAmount.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    public string? NetAmountExact => NetAmount?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    public string? VatAmountExact => VatAmount?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
    public string? VatAmountPlnExact => VatAmountPln?.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
}
