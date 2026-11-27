namespace xKsef.Domain.SalesInvoices.Calculation;

public sealed record CalculatedInvoiceLine(Guid Id, decimal LineDiscount, decimal GlobalDiscountAllocation, PriceBasis AllocationBasis, decimal Net, decimal Vat, decimal Gross, decimal VatPln, decimal VatRoundingAdjustment);
public sealed record CalculatedTaxSummary(string TaxCode, decimal Net, decimal Vat, decimal Gross, decimal VatPln);
public sealed record InvoiceCalculationResult(bool IsComplete, IReadOnlyList<InvoiceValidationIssue> MissingData, IReadOnlyList<CalculatedInvoiceLine> Lines, IReadOnlyList<CalculatedTaxSummary> TaxSummaries, decimal? Net, decimal? Vat, decimal? Gross, decimal? VatPln, decimal GlobalDiscountAmount)
{
    public const string AlgorithmVersion = "sales-v1";
}
