namespace xKsef.Domain.Invoices;

public class StoredInvoice
{
    public long Id { get; set; }

    public required string KsefNumber { get; set; }

    public required string Xml { get; set; }

    public DateTimeOffset ImportedAt { get; set; }
}