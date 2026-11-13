namespace xKsef.Contracts.Invoices;

public sealed record InvoiceCurrencySummaryDto(string Currency, int InvoiceCount, decimal? NetTotal, decimal? VatTotal, decimal GrossTotal, int MissingNetCount, int MissingVatCount);
