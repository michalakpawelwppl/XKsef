namespace xKsef.Contracts.Invoices;

public sealed class InvoicePackageReadResult
{
    public required string ReferenceNumber { get; init; }

    public required int DeclaredInvoiceCount { get; init; }

    public required int ReadInvoiceCount { get; init; }

    public required int ImportedCount { get; init; }

    public required int SkippedCount { get; init; }
}