namespace xKsef.Domain.Invoices;

public sealed class StoredInvoice
{
    public string InvoiceNumber { get; set; } = string.Empty;

    public DateOnly IssueDate { get; set; }

    public InvoiceDirection Direction { get; set; }

    public decimal GrossAmount { get; set; }

    public string Currency { get; set; } = string.Empty;

    public string SellerName { get; set; } = string.Empty;

    public string SellerNip { get; set; } = string.Empty;
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