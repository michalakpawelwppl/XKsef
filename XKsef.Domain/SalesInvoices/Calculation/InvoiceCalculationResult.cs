namespace xKsef.Domain.SalesInvoices.Calculation;

public enum VatAmountOrigin { PerLineCalculation = 0, TaxGroupAllocation = 1 }
// TaxBase is the sum of rounded line bases for positional methods, or the rounded group base for aggregate methods.
public sealed record VatCalculationDetails(int Version, VatCalculationMethod Method, PriceBasis Basis, decimal TaxBase, decimal Rate);
public sealed record CalculatedInvoiceLine(Guid Id, decimal LineDiscount, decimal GlobalDiscountAllocation, PriceBasis AllocationBasis, decimal Net, decimal Vat, decimal Gross, decimal VatPln, decimal VatRoundingAdjustment,
    VatAmountOrigin? VatAmountOrigin = null, decimal? VatBaseRoundingAdjustment = null);
public sealed record CalculatedTaxSummary(string TaxCode, decimal Net, decimal Vat, decimal Gross, decimal VatPln, VatCalculationDetails? CalculationDetails = null);
public sealed record InvoiceCalculationResult(bool IsComplete, IReadOnlyList<InvoiceValidationIssue> MissingData, IReadOnlyList<CalculatedInvoiceLine> Lines, IReadOnlyList<CalculatedTaxSummary> TaxSummaries, decimal? Net, decimal? Vat, decimal? Gross, decimal? VatPln, decimal GlobalDiscountAmount)
{
    public const string AlgorithmVersion = "sales-v3";
}
