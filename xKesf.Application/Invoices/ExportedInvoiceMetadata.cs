namespace xKsef.Application.Invoices;

public sealed class ExportedInvoiceMetadata
{
    public required string KsefNumber { get; init; }

    public required string FileName { get; init; }

    public required string InvoiceHash { get; init; }
}