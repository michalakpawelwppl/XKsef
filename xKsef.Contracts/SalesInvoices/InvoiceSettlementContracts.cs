using System.Text.Json.Serialization;

namespace xKsef.Contracts.SalesInvoices;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InvoiceSettlementEntryDto(ExactDecimal? Amount = null, string? Reason = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InvoiceSettlementDto
{
    public IReadOnlyList<InvoiceSettlementEntryDto> Charges { get; init; } = [];
    public IReadOnlyList<InvoiceSettlementEntryDto> Deductions { get; init; } = [];
}
public sealed record InvoiceSettlementAmountsDto(ExactDecimal? ChargesTotal, ExactDecimal? DeductionsTotal, ExactDecimal? AmountDue, ExactDecimal? Overpayment);
