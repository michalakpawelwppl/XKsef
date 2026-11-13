namespace xKsef.Domain.Invoices;

public sealed class StoredInvoice
{
    public Guid TenantId { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;

    public DateOnly IssueDate { get; set; }

    public InvoiceDirection Direction { get; set; }

    public decimal? NetAmount { get; set; }

    public decimal? VatAmount { get; set; }

    public decimal? VatAmountPln { get; set; }

    public DateTimeOffset? InvoicingDate { get; set; }

    public decimal GrossAmount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string SellerName { get; set; } = string.Empty;

    public string SellerNip { get; set; } = string.Empty;
    public string? BuyerNip { get; set; }

    public string? BuyerName { get; set; }

    private byte[] _xmlBytes = [];

    public long Id { get; set; }

    public required string KsefNumber { get; init; }

    public DateTimeOffset ImportedAt { get; init; }

    public byte[] XmlBytes
    {
        get => (byte[])_xmlBytes.Clone();
        init => _xmlBytes = value is not null
            ? (byte[])value.Clone()
            : throw new ArgumentNullException(nameof(value));
    }

}