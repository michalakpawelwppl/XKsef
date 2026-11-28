using xKsef.Application.SalesInvoices;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Tests.Domain;

public sealed class FourVatCalculationTests
{
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-000000000003");
    private static SalesInvoiceLineInput Line(Guid id, decimal price, PriceBasis basis = PriceBasis.Net, string code = "23") => SalesInvoiceCalculationTests.Line(id, price, basis: basis, tax: code);

    [Theory]
    [InlineData(VatCalculationMethod.PerLineNet, PriceBasis.Net, "0.09", "0.03", "0.12")]
    [InlineData(VatCalculationMethod.PerLineGross, PriceBasis.Net, "0.09", "0.03", "0.12")]
    [InlineData(VatCalculationMethod.ByNetTotals, PriceBasis.Net, "0.09", "0.02", "0.11")]
    [InlineData(VatCalculationMethod.ByGrossTotals, PriceBasis.Net, "0.09", "0.02", "0.11")]
    [InlineData(VatCalculationMethod.PerLineNet, PriceBasis.Gross, "0.06", "0.00", "0.06")]
    [InlineData(VatCalculationMethod.PerLineGross, PriceBasis.Gross, "0.06", "0.03", "0.09")]
    [InlineData(VatCalculationMethod.ByNetTotals, PriceBasis.Gross, "0.07", "0.02", "0.09")]
    [InlineData(VatCalculationMethod.ByGrossTotals, PriceBasis.Gross, "0.07", "0.02", "0.09")]
    public void ThreeThreeCentLinesDistinguishAllMethodsAndInputPriceBases(VatCalculationMethod method, PriceBasis basis, string net, string vat, string gross)
    {
        var result = new SalesInvoiceCalculator().Calculate(new() { VatMethod = method, Lines = [Line(A, .03m, basis), Line(B, .03m, basis), Line(C, .03m, basis)] });
        Assert.Equal(Parse(net), result.Net); Assert.Equal(Parse(vat), result.Vat); Assert.Equal(Parse(gross), result.Gross);
        AssertConsistent(result);
        var allocated = method is VatCalculationMethod.ByNetTotals or VatCalculationMethod.ByGrossTotals;
        Assert.All(result.Lines, line => Assert.Equal(allocated ? VatAmountOrigin.TaxGroupAllocation : VatAmountOrigin.PerLineCalculation, line.VatAmountOrigin));
        Assert.Equal(method, Assert.Single(result.TaxSummaries).CalculationDetails!.Method);
    }

    [Theory]
    [InlineData(VatCalculationMethod.ByNetTotals, PriceBasis.Net)]
    [InlineData(VatCalculationMethod.ByGrossTotals, PriceBasis.Gross)]
    public void AggregateTaxSumsBeforeRoundingTheGroupBaseAndAllocatesItSeparately(VatCalculationMethod method, PriceBasis basis)
    {
        var lines = Enumerable.Range(1, 10).Select(x => Line(Guid.Parse($"00000000-0000-0000-0000-{x:D12}"), .004m, basis)).ToArray();
        var draft = new SalesInvoiceDraft { VatMethod = method, Lines = lines };
        var result = new SalesInvoiceCalculator().Calculate(draft);
        Assert.Equal(.01m, result.Vat); Assert.Equal(.04m, Assert.Single(result.TaxSummaries).CalculationDetails!.TaxBase);
        Assert.Equal(.04m, result.Lines.Sum(x => x.VatBaseRoundingAdjustment));
        Assert.Equal(.04m, basis == PriceBasis.Net ? result.Net : result.Gross);
        AssertConsistent(result);
        var reverse = new SalesInvoiceCalculator().Calculate(draft with { Lines = lines.Reverse().ToArray() });
        Assert.Equal(result.Lines, reverse.Lines); Assert.Equal(result.TaxSummaries, reverse.TaxSummaries);
    }

    [Theory]
    [InlineData(VatCalculationMethod.PerLineNet, PriceBasis.Net, "0.024", "0.02", "0.00", "0.02")]
    [InlineData(VatCalculationMethod.PerLineGross, PriceBasis.Net, "0.024", "0.02", "0.01", "0.03")]
    [InlineData(VatCalculationMethod.ByNetTotals, PriceBasis.Net, "0.024", "0.02", "0.00", "0.02")]
    [InlineData(VatCalculationMethod.ByGrossTotals, PriceBasis.Net, "0.024", "0.02", "0.01", "0.03")]
    [InlineData(VatCalculationMethod.PerLineNet, PriceBasis.Gross, "0.025", "0.02", "0.00", "0.02")]
    [InlineData(VatCalculationMethod.PerLineGross, PriceBasis.Gross, "0.025", "0.02", "0.01", "0.03")]
    [InlineData(VatCalculationMethod.ByNetTotals, PriceBasis.Gross, "0.025", "0.02", "0.00", "0.02")]
    [InlineData(VatCalculationMethod.ByGrossTotals, PriceBasis.Gross, "0.025", "0.02", "0.01", "0.03")]
    public void TaxFormulaUsesTwoDecimalBaseIndependentlyOfInputPriceBasis(VatCalculationMethod method, PriceBasis basis, string price, string net, string vat, string gross)
    {
        var input = Line(A, Parse(price), basis);
        var result = new SalesInvoiceCalculator().Calculate(new() { VatMethod = method, Lines = [input] });
        Assert.Equal(Parse(net), result.Net); Assert.Equal(Parse(vat), result.Vat); Assert.Equal(Parse(gross), result.Gross);
        var details = Assert.Single(result.TaxSummaries).CalculationDetails!;
        Assert.Equal(method is VatCalculationMethod.PerLineNet or VatCalculationMethod.ByNetTotals ? Parse(net) : Parse(gross), details.TaxBase);
        Assert.Equal(Parse(price), input.UnitPrice); Assert.Equal(basis, input.PriceBasis); AssertConsistent(result);
    }

    [Theory]
    [InlineData(VatCalculationMethod.ByNetTotals, PriceBasis.Net, "0.008", "0.008", "0.008", "0.02", "0.00", "0.02")]
    [InlineData(VatCalculationMethod.ByNetTotals, PriceBasis.Net, "0.0215", "0.0215", "0.022", "0.07", "0.02", "0.09")]
    [InlineData(VatCalculationMethod.ByGrossTotals, PriceBasis.Gross, "0.008", "0.008", "0.009", "0.02", "0.01", "0.03")]
    public void GroupBaseIsRoundedAfterSummingAndBeforeCalculatingVat(VatCalculationMethod method, PriceBasis basis, string a, string b, string c, string net, string vat, string gross)
    {
        var draft = new SalesInvoiceDraft { VatMethod = method, Lines = [Line(A, Parse(a), basis), Line(B, Parse(b), basis), Line(C, Parse(c), basis)] };
        var result = new SalesInvoiceCalculator().Calculate(draft);
        Assert.Equal(Parse(net), result.Net); Assert.Equal(Parse(vat), result.Vat); Assert.Equal(Parse(gross), result.Gross);
        Assert.Equal(basis == PriceBasis.Net ? Parse(net) : Parse(gross), Assert.Single(result.TaxSummaries).CalculationDetails!.TaxBase);
        AssertConsistent(result);
        var reverse = new SalesInvoiceCalculator().Calculate(draft with { Lines = draft.Lines.Reverse().ToArray() });
        Assert.Equal(result.Lines, reverse.Lines); Assert.Equal(result.TaxSummaries, reverse.TaxSummaries);
    }

    [Theory]
    [InlineData(VatCalculationMethod.PerLineGross)]
    [InlineData(VatCalculationMethod.ByGrossTotals)]
    public void GrossTaxUsesDirectFormulaWithoutCalculatingVatFromRoundedNet(VatCalculationMethod method)
    {
        var result = new SalesInvoiceCalculator().Calculate(new() { VatMethod = method, Lines = [Line(A, .08m, PriceBasis.Gross)] });
        // Round2(.08 * 23 / 123) = .01; Round2(Round2(.08 / 1.23) * .23) would incorrectly give .02.
        Assert.Equal(.01m, result.Vat); Assert.Equal(.07m, result.Net); Assert.Equal(.08m, result.Gross); AssertConsistent(result);
    }

    [Theory]
    [InlineData(VatCalculationMethod.PerLineNet)]
    [InlineData(VatCalculationMethod.PerLineGross)]
    [InlineData(VatCalculationMethod.ByNetTotals)]
    [InlineData(VatCalculationMethod.ByGrossTotals)]
    public void PrecisePriceQuantityAndDiscountArePreservedUntilTheTaxBaseIsRounded(VatCalculationMethod method)
    {
        var line = Line(A, 12.34567891m) with { Quantity = 2.345678m, Discount = new(DiscountKind.Percent, 12.5m) };
        var result = new SalesInvoiceCalculator().Calculate(new() { VatMethod = method, Lines = [line] });
        Assert.Equal(3.6198734267813725m, result.Lines[0].LineDiscount);
        Assert.Equal(25.34m, result.Net); Assert.Equal(5.83m, result.Vat); Assert.Equal(31.17m, result.Gross);
        Assert.Equal(method is VatCalculationMethod.PerLineNet or VatCalculationMethod.ByNetTotals ? 25.34m : 31.17m, Assert.Single(result.TaxSummaries).CalculationDetails!.TaxBase);
        AssertConsistent(result);
    }

    [Fact]
    public void GrossAllocationNeverTaxesALineWithZeroAllocatedBase()
    {
        var result = new SalesInvoiceCalculator().Calculate(new() { VatMethod = VatCalculationMethod.ByGrossTotals,
            Lines = [Line(A, .004m, PriceBasis.Gross), Line(B, .004m, PriceBasis.Gross), Line(C, .018m, PriceBasis.Gross)] });
        Assert.Equal(.03m, result.Gross); Assert.Equal(.01m, result.Vat); Assert.Equal(.02m, result.Net);
        Assert.All(result.Lines.Where(x => x.Gross == 0), x => Assert.Equal(0m, x.Vat)); AssertConsistent(result);
    }

    [Theory]
    [InlineData(VatCalculationMethod.PerLineNet)]
    [InlineData(VatCalculationMethod.PerLineGross)]
    [InlineData(VatCalculationMethod.ByNetTotals)]
    [InlineData(VatCalculationMethod.ByGrossTotals)]
    public void MixedPricesAndTaxRatesKeepIndependentInputsAndRateSummaries(VatCalculationMethod method)
    {
        var draft = new SalesInvoiceDraft { VatMethod = method, Lines = [Line(A, 100), Line(B, 108, PriceBasis.Gross, "8"), Line(C, 20, code: "5")] };
        var result = new SalesInvoiceCalculator().Calculate(draft);
        Assert.Equal(220m, result.Net); Assert.Equal(32m, result.Vat); Assert.Equal(252m, result.Gross);
        Assert.Equal(new[] { "23", "5", "8" }, result.TaxSummaries.Select(x => x.TaxCode));
        Assert.Equal(PriceBasis.Net, draft.Lines[0].PriceBasis); Assert.Equal(PriceBasis.Gross, draft.Lines[1].PriceBasis);
        AssertConsistent(result);
        var discounted = new SalesInvoiceCalculator().Calculate(draft with { GlobalDiscount = new(DiscountKind.Percent, 10) });
        Assert.Equal(198m, discounted.Net); Assert.Equal(28.80m, discounted.Vat); Assert.Equal(226.80m, discounted.Gross);
        Assert.Equal(22m, discounted.GlobalDiscountAmount); AssertConsistent(discounted);
    }

    [Theory]
    [InlineData(VatCalculationMethod.PerLineNet)]
    [InlineData(VatCalculationMethod.PerLineGross)]
    [InlineData(VatCalculationMethod.ByNetTotals)]
    [InlineData(VatCalculationMethod.ByGrossTotals)]
    public void FixedDocumentDiscountAndFullDiscountHaveExactDeterministicMoneyShares(VatCalculationMethod method)
    {
        var draft = new SalesInvoiceDraft { VatMethod = method, Lines = [Line(A, .03m), Line(B, .03m), Line(C, .03m)], GlobalDiscount = new(DiscountKind.Amount, .01m) };
        var result = new SalesInvoiceCalculator().Calculate(draft);
        Assert.Equal(.08m, result.Net); Assert.Equal(.02m, result.Vat); Assert.Equal(.10m, result.Gross);
        Assert.Equal(.01m, result.Lines.Single(x => x.Id == A).GlobalDiscountAllocation);
        Assert.Equal(0m, result.Lines.Single(x => x.Id == B).GlobalDiscountAllocation); AssertConsistent(result);
        var fullyDiscounted = new SalesInvoiceCalculator().Calculate(draft with { GlobalDiscount = new(DiscountKind.Percent, 100) });
        Assert.Equal(0m, fullyDiscounted.Gross); AssertConsistent(fullyDiscounted);
    }

    [Theory]
    [InlineData(VatCalculationMethod.PerLineNet)]
    [InlineData(VatCalculationMethod.PerLineGross)]
    [InlineData(VatCalculationMethod.ByNetTotals)]
    [InlineData(VatCalculationMethod.ByGrossTotals)]
    public void HalfCentVatRoundsUpWithoutRoundingUnitPriceOrQuantityFirst(VatCalculationMethod method)
    {
        var result = new SalesInvoiceCalculator().Calculate(new() { VatMethod = method, Lines = [Line(A, .123m, PriceBasis.Gross) with { Quantity = 5m }] });
        Assert.Equal(.12m, result.Vat); Assert.Equal(.50m, result.Net); Assert.Equal(.62m, result.Gross); AssertConsistent(result);
    }

    [Theory]
    [InlineData(VatCalculationMethod.PerLineNet)]
    [InlineData(VatCalculationMethod.PerLineGross)]
    [InlineData(VatCalculationMethod.ByNetTotals)]
    [InlineData(VatCalculationMethod.ByGrossTotals)]
    public void FullDiscountAlwaysRemovesTheBaseEvenWhenItsNetMoneyAmountRoundsToZero(VatCalculationMethod method)
    {
        var result = new SalesInvoiceCalculator().Calculate(new() { VatMethod = method, Lines = [Line(A, .004945m)], GlobalDiscount = new(DiscountKind.Percent, 100) });
        Assert.Equal(0m, result.Net); Assert.Equal(0m, result.Vat); Assert.Equal(0m, result.Gross);
        Assert.Equal(0m, Assert.Single(result.TaxSummaries).CalculationDetails!.TaxBase); AssertConsistent(result);
    }

    [Theory]
    [InlineData(VatCalculationMethod.PerLineNet)]
    [InlineData(VatCalculationMethod.PerLineGross)]
    [InlineData(VatCalculationMethod.ByNetTotals)]
    [InlineData(VatCalculationMethod.ByGrossTotals)]
    public void SpecialProceduresNeverUseStandardVatFormulas(VatCalculationMethod method)
    {
        foreach (var special in new[] { InvoiceTaxTreatmentKind.Margin, InvoiceTaxTreatmentKind.UnionOss })
        {
            var result = new SalesInvoiceCalculator().Calculate(new() { VatMethod = method, Lines = [Line(A, 100) with { TaxTreatment = new() { Kind = special } }] });
            Assert.False(result.IsComplete); Assert.Null(result.Vat); Assert.Empty(result.TaxSummaries);
            Assert.Contains(result.MissingData, x => x.Code == "TaxTreatmentCalculationNotImplemented");
        }
        var marginDocument = new SalesInvoiceCalculator().Calculate(new() { VatMethod = method, Lines = [Line(A, 100)], Fa3 = new() { Qualifications = new() { HasMarginSales = new() { Value = QualificationValue.Yes } } } });
        Assert.False(marginDocument.IsComplete); Assert.Contains(marginDocument.MissingData, x => x.Code == "MarginCalculationNotImplemented");
    }

    [Fact]
    public void TechnicalAllocationAndItsMetadataAreValidatedIndependentlyOfUnchangedMoneyAmounts()
    {
        var invoice = SalesInvoice.Create(Guid.NewGuid(), new() { VatMethod = VatCalculationMethod.ByNetTotals, Lines = [Line(A, .03m), Line(B, .03m), Line(C, .03m)] }, "user", DateTimeOffset.UtcNow, new());
        typeof(SalesInvoiceLine).GetProperty(nameof(SalesInvoiceLine.VatAmountOrigin))!.SetValue(invoice.Lines[0], VatAmountOrigin.PerLineCalculation);
        typeof(SalesInvoiceTaxSummary).GetProperty(nameof(SalesInvoiceTaxSummary.CalculationDetails))!.SetValue(invoice.TaxSummaries[0], invoice.TaxSummaries[0].CalculationDetails! with { Rate = 22 });
        var errors = new SalesInvoiceIssuanceValidator(new()).Validate(invoice).Errors;
        Assert.Contains(errors, x => x.Code == "LineAmountsMismatch"); Assert.Contains(errors, x => x.Code == "TaxSummariesMismatch");
    }

    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static void AssertConsistent(InvoiceCalculationResult result)
    {
        Assert.True(result.IsComplete); Assert.Equal(result.Net + result.Vat, result.Gross);
        Assert.Equal(result.Net, result.Lines.Sum(x => x.Net)); Assert.Equal(result.Vat, result.Lines.Sum(x => x.Vat));
        Assert.Equal(result.Gross, result.Lines.Sum(x => x.Gross)); Assert.Equal(result.Vat, result.TaxSummaries.Sum(x => x.Vat));
        Assert.Equal(result.Net, result.TaxSummaries.Sum(x => x.Net)); Assert.Equal(result.Gross, result.TaxSummaries.Sum(x => x.Gross));
        Assert.Equal(result.VatPln, result.Lines.Sum(x => x.VatPln)); Assert.Equal(result.VatPln, result.TaxSummaries.Sum(x => x.VatPln));
        foreach (var summary in result.TaxSummaries)
        {
            Assert.NotNull(summary.CalculationDetails);
            Assert.Equal(summary.Net + summary.Vat, summary.Gross);
        }
        Assert.All(result.Lines, x => { Assert.Equal(x.Net + x.Vat, x.Gross); Assert.True(x.Net >= 0); Assert.True(x.Vat >= 0); });
    }
}
