namespace xKsef.Contracts.Invoices;

public sealed record InvoiceQueryResultDto(IReadOnlyList<InvoiceDto> Items, IReadOnlyList<InvoiceCurrencySummaryDto> Summaries);
