namespace xKsef.Application.Invoices;

public sealed record InvoiceImportResult(
    int ImportedCount,
    int SkippedCount);