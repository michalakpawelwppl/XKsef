internal sealed class InvoicePackageReadResult
{
    public required string ReferenceNumber { get; init; }

    public required int DeclaredInvoiceCount { get; init; }

    public required int ReadInvoiceCount { get; init; }
}