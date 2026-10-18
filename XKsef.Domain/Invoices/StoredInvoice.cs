namespace xKsef.Domain.Invoices;

public sealed class StoredInvoice
{
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