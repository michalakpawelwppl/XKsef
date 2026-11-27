using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Tests.Domain;

public sealed class SalesInvoiceCalculationTests
{
    private readonly SalesInvoiceCalculator _calculator = new();
    private static readonly Guid A = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid B = Guid.Parse("00000000-0000-0000-0000-000000000002");
    private static readonly Guid C = Guid.Parse("00000000-0000-0000-0000-000000000003");
    public static SalesInvoiceLineInput Line(Guid id, decimal? price = 100m, decimal? quantity = 1m, PriceBasis basis = PriceBasis.Net, string tax = "23") => new() { Id = id, Description = "Test", Unit = "kg", UnitPrice = price, Quantity = quantity, PriceBasis = basis, TaxCode = tax };

    [Theory]
    [InlineData(VatCalculationMethod.PerLine, "0.06", "0.02", "0.08")]
    [InlineData(VatCalculationMethod.ByNetTotals, "0.06", "0.01", "0.07")]
    [InlineData(VatCalculationMethod.ByGrossTotals, "0.07", "0.01", "0.08")]
    public void MethodsHaveTheirOwnRoundingSemantics(VatCalculationMethod method, string net, string vat, string gross)
    {
        var result = _calculator.Calculate(new() { VatMethod = method, Lines = [Line(A, .03m), Line(B, .03m)] });
        Assert.Equal(Parse(net), result.Net); Assert.Equal(Parse(vat), result.Vat); Assert.Equal(Parse(gross), result.Gross);
        AssertConsistent(result);
    }

    [Theory]
    [InlineData(VatCalculationMethod.PerLine)]
    [InlineData(VatCalculationMethod.ByNetTotals)]
    [InlineData(VatCalculationMethod.ByGrossTotals)]
    public void MixedPricesMultipleRatesAndGlobalDiscount(VatCalculationMethod method)
    {
        var draft = new SalesInvoiceDraft { VatMethod = method, GlobalDiscount = new(DiscountKind.Percent, 10m), Lines = [Line(A, 100m, 2m), Line(B, 108m, 1m, PriceBasis.Gross, "8"), Line(C, 50m, 1m, PriceBasis.Net, "zw")] };
        var result = _calculator.Calculate(draft);
        if (method == VatCalculationMethod.ByGrossTotals)
        {
            Assert.Equal(40.40m, result.GlobalDiscountAmount);
        }
        else Assert.Equal(35m, result.GlobalDiscountAmount);
        Assert.Equal(315m, result.Net); Assert.Equal(48.60m, result.Vat); Assert.Equal(363.60m, result.Gross);
        Assert.Equal(3, result.TaxSummaries.Count); AssertConsistent(result);
    }

    [Theory]
    [InlineData("100.12345678", "1250", "125154.32")]
    [InlineData("100123.45678000", "1.25", "125154.32")]
    [InlineData("0.12345678", "999.999999", "123.46")]
    [InlineData("100.12345678", "0.123456", "12.36")]
    public void DoesNotRoundUnitPriceBeforeMultiplication(string price, string quantity, string expected)
    {
        var result = _calculator.Calculate(new() { Lines = [Line(A, Parse(price), Parse(quantity))] });
        Assert.Equal(Parse(expected), result.Net); AssertConsistent(result);
    }

    [Fact]
    public void PercentageDiscountKeepsIntermediatePrecision()
    {
        var result = _calculator.Calculate(new() { Lines = [Line(A, 12.34567891m, 2.345678m) with { Discount = new(DiscountKind.Percent, 12.5m) }] });
        Assert.Equal(25.34m, result.Net); Assert.Equal(5.83m, result.Vat); Assert.Equal(31.17m, result.Gross);
        Assert.Equal(3.6198734267813725m, result.Lines[0].LineDiscount);
    }

    [Fact]
    public void AmountDiscountAppliesToWholeLineNotEachUnit()
    {
        var result = _calculator.Calculate(new() { Lines = [Line(A, 10m, 1.5m) with { Discount = new(DiscountKind.Amount, 2m) }] });
        Assert.Equal(13m, result.Net); Assert.Equal(2.99m, result.Vat);
    }

    [Fact]
    public void GrossGlobalDiscountKeepsItsExactMoneyBasisForNetInputPrices()
    {
        var draft = new SalesInvoiceDraft { Lines = [Line(A, .03m), Line(B, .03m)], GlobalDiscountBasis = PriceBasis.Gross };
        var before = _calculator.Calculate(draft);
        var after = _calculator.Calculate(draft with { GlobalDiscount = new(DiscountKind.Amount, .01m) });
        Assert.Equal(.08m, before.Gross); Assert.Equal(.07m, after.Gross);
        Assert.Equal(.01m, before.Gross - after.Gross); AssertConsistent(after);
    }

    [Fact]
    public void NetGlobalDiscountKeepsItsExactMoneyBasisForGrossInputPrices()
    {
        var draft = new SalesInvoiceDraft { Lines = [Line(A, .03m, basis: PriceBasis.Gross), Line(B, .03m, basis: PriceBasis.Gross)] };
        var before = _calculator.Calculate(draft);
        var after = _calculator.Calculate(draft with { GlobalDiscount = new(DiscountKind.Amount, .01m) });
        Assert.Equal(.04m, before.Net); Assert.Equal(.03m, after.Net); AssertConsistent(after);
    }

    [Fact]
    public void DiscountRoundedToZeroDoesNotChangeThePriceRoundingBasis()
    {
        var draft = new SalesInvoiceDraft { Lines = [Line(A, .03m, basis: PriceBasis.Gross), Line(B, .03m, basis: PriceBasis.Gross)] };
        var before = _calculator.Calculate(draft);
        var after = _calculator.Calculate(draft with { GlobalDiscount = new(DiscountKind.Percent, .000001m) });
        Assert.Equal(before.Net, after.Net); Assert.Equal(before.Vat, after.Vat); Assert.Equal(before.Gross, after.Gross);
    }

    [Fact]
    public void GroupTaxRemaindersComeFromUnroundedTaxNotRenormalizedRoundedTarget()
    {
        var result = _calculator.Calculate(new() { VatMethod = VatCalculationMethod.ByNetTotals, Lines = [Line(A, 13.06m), Line(B, .02m)] });
        Assert.Equal(3.01m, result.Vat);
        Assert.Equal(3.00m, result.Lines.Single(x => x.Id == A).Vat);
        Assert.Equal(.01m, result.Lines.Single(x => x.Id == B).Vat);
    }

    [Theory]
    [InlineData(VatCalculationMethod.PerLine, PriceBasis.Net)]
    [InlineData(VatCalculationMethod.PerLine, PriceBasis.Gross)]
    [InlineData(VatCalculationMethod.ByNetTotals, PriceBasis.Net)]
    [InlineData(VatCalculationMethod.ByGrossTotals, PriceBasis.Gross)]
    public void ReorderingNeverChangesAllocations(VatCalculationMethod method, PriceBasis basis)
    {
        var lines = new[] { Line(C, .11m), Line(A, .11m), Line(B, .11m) };
        var draft = new SalesInvoiceDraft { VatMethod = method, GlobalDiscountBasis = basis, GlobalDiscount = new(DiscountKind.Amount, .01m), Lines = lines };
        var first = _calculator.Calculate(draft);
        foreach (var permutation in new[] { lines.Reverse().ToArray(), new[] { lines[1], lines[0], lines[2] } })
            Assert.Equal(first, _calculator.Calculate(draft with { Lines = permutation }), InvoiceResultComparer.Instance);
        Assert.Equal(.01m, first.Lines.Sum(x => x.GlobalDiscountAllocation));
        Assert.Equal(.01m, first.Lines.Single(x => x.Id == A).GlobalDiscountAllocation);
    }

    [Fact]
    public void AllocationHandlesAmountsWhoseNaiveProductExceedsDecimal()
    {
        var allocations = FinancialMath.Allocate(9999999999999999.99m, new Dictionary<Guid, decimal> { [A] = 9999999999999999.99m, [B] = 9999999999999999.99m });
        Assert.Equal(5000000000000000m, allocations[A]); Assert.Equal(4999999999999999.99m, allocations[B]);
    }

    [Fact]
    public void CurrencyVatIsRoundedAtTaxGroupBeforeDistribution()
    {
        var draft = new SalesInvoiceDraft { Currency = "EUR", Lines = [Line(A, .05m), Line(B, .05m), Line(C, .05m)], ExchangeRates = [Rate(4.321234m)] };
        var result = _calculator.Calculate(draft);
        Assert.Equal(.03m, result.Vat); Assert.Equal(.13m, result.VatPln);
        Assert.Equal(.05m, result.Lines.Single(x => x.Id == A).VatPln); AssertConsistent(result);
    }

    [Fact]
    public void TwelveDigitExchangeRateIsNotTruncatedToXsdPrecision()
    {
        var result = _calculator.Calculate(new() { Currency = "EUR", Lines = [Line(A, 500000m, tax: "8")], ExchangeRates = [Rate(4.123456789012m)] });
        Assert.Equal(164938.27m, result.VatPln);
    }

    [Fact]
    public void MissingFinancialDataIsNotReportedAsZero()
    {
        var result = _calculator.Calculate(new() { Currency = "EUR", Lines = [Line(A, null, null)] });
        Assert.False(result.IsComplete); Assert.Null(result.Net); Assert.Null(result.Gross); Assert.Empty(result.Lines); Assert.Equal(3, result.MissingData.Count);
    }

    [Theory]
    [InlineData("23")][InlineData("22")][InlineData("8")][InlineData("7")][InlineData("5")][InlineData("4")][InlineData("3")]
    [InlineData("0 KR")][InlineData("0 WDT")][InlineData("0 EX")][InlineData("zw")][InlineData("np I")][InlineData("np II")][InlineData("oo")]
    public void SupportedTreatmentsStayDistinct(string code)
    {
        var result = _calculator.Calculate(new() { Lines = [Line(A, tax: code)] });
        Assert.Equal(code, Assert.Single(result.TaxSummaries).TaxCode); AssertConsistent(result);
    }

    [Theory]
    [InlineData(-1, 1)][InlineData(1, -1)]
    public void NegativeOrdinarySalesInputsAreRejected(int price, int quantity) => Assert.Throws<SalesInvoiceValidationException>(() => _calculator.Calculate(new() { Lines = [Line(A, price, quantity)] }));

    [Fact]
    public void InvalidPrecisionAndExcessDiscountAreRejected()
    {
        foreach (var line in new[] { Line(A, 1.000000001m), Line(A, quantity: 1.0000001m), Line(A) with { Discount = new(DiscountKind.Percent, 101m) }, Line(A) with { Discount = new(DiscountKind.Amount, 101m) } })
            Assert.Throws<SalesInvoiceValidationException>(() => _calculator.Calculate(new() { Lines = [line] }));
        Assert.Throws<SalesInvoiceValidationException>(() => _calculator.Calculate(new() { Lines = [Line(A, decimal.MaxValue)] }));
    }

    [Fact]
    public void FullDiscountAndZeroBasesDoNotDivideByZero()
    {
        var result = _calculator.Calculate(new() { Lines = [Line(A), Line(B, 0)], GlobalDiscount = new(DiscountKind.Percent, 100) });
        Assert.Equal(0m, result.Gross); AssertConsistent(result);
        Assert.Throws<SalesInvoiceValidationException>(() => _calculator.Calculate(new() { Lines = [Line(A, 0)], GlobalDiscount = new(DiscountKind.Amount, .01m) }));
    }

    [Fact]
    public void SnapshotListsCannotBeChangedThroughTheOriginalDraft()
    {
        var contacts = new List<InvoiceContact> { new("first@example.com", null) };
        var invoice = SalesInvoice.Create(Guid.NewGuid(), new() { Seller = new() { Contacts = contacts } }, "user", DateTimeOffset.UtcNow, _calculator);
        contacts.Clear();
        Assert.Single(invoice.ToDraft().Seller!.Contacts);
    }

    [Theory]
    [InlineData(VatCalculationMethod.PerLine)]
    [InlineData(VatCalculationMethod.ByNetTotals)]
    [InlineData(VatCalculationMethod.ByGrossTotals)]
    public void RepeatedMixedInputCasesPreserveInvariantsAndPermutationIndependence(VatCalculationMethod method)
    {
        var random = new Random(29031);
        for (var i = 0; i < 40; i++)
        {
            var draft = new SalesInvoiceDraft
            {
                VatMethod = method, GlobalDiscountBasis = i % 2 == 0 ? PriceBasis.Net : PriceBasis.Gross,
                GlobalDiscount = new(DiscountKind.Percent, i % 21),
                Lines = [Line(A, random.Next(1, 100000) / 100m, random.Next(1, 1000) / 100m), Line(B, random.Next(1, 100000) / 100m, random.Next(1, 1000) / 100m, PriceBasis.Gross, "8"), Line(C, random.Next(1, 100000) / 100m, 1m, PriceBasis.Net, "zw")]
            };
            var result = _calculator.Calculate(draft); AssertConsistent(result);
            Assert.Equal(result, _calculator.Calculate(draft with { Lines = draft.Lines.Reverse().ToArray() }), InvoiceResultComparer.Instance);
            Assert.Equal(result.GlobalDiscountAmount, result.Lines.Sum(x => x.GlobalDiscountAllocation));
        }
    }

    [Fact]
    public void ChangingVatMethodNormalizesDiscountBasisWithoutTouchingSourcePrices()
    {
        var source = new SalesInvoiceDraft { Lines = [Line(A, 100.12345678m)], GlobalDiscountBasis = PriceBasis.Gross };
        var invoice = SalesInvoice.Create(Guid.NewGuid(), source, "user", DateTimeOffset.UtcNow, _calculator);
        foreach (var method in new[] { VatCalculationMethod.ByNetTotals, VatCalculationMethod.ByGrossTotals, VatCalculationMethod.PerLine })
        {
            invoice.UpdateDraft(invoice.ToDraft() with { VatMethod = method }, "user", DateTimeOffset.UtcNow, _calculator);
            Assert.Equal(method == VatCalculationMethod.ByNetTotals ? PriceBasis.Net : PriceBasis.Gross, invoice.GlobalDiscountBasis);
            Assert.Equal(100.12345678m, invoice.Lines[0].UnitPrice); Assert.Equal(A, invoice.Lines[0].Id);
        }
    }

    [Theory]
    [InlineData(SalesInvoiceStatus.Approved)][InlineData(SalesInvoiceStatus.Issued)]
    public void DraftCommandsCannotModifyOtherStatuses(SalesInvoiceStatus status)
    {
        var invoice = SalesInvoice.Create(Guid.NewGuid(), new(), "user", DateTimeOffset.UtcNow, _calculator);
        typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.Status))!.SetValue(invoice, status);
        Assert.Throws<SalesInvoiceConflictException>(() => invoice.UpdateDraft(new(), "user", DateTimeOffset.UtcNow, _calculator));
    }

    public static ExchangeRateData Rate(decimal value) => new() { Id = Guid.NewGuid(), Rate = value, RateDate = new DateOnly(2026, 11, 26) };
    private static decimal Parse(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
    private static void AssertConsistent(InvoiceCalculationResult result)
    {
        Assert.True(result.IsComplete); Assert.Equal(result.Net + result.Vat, result.Gross);
        Assert.Equal(result.Net, result.Lines.Sum(x => x.Net)); Assert.Equal(result.Vat, result.Lines.Sum(x => x.Vat));
        Assert.Equal(result.Gross, result.Lines.Sum(x => x.Gross)); Assert.Equal(result.VatPln, result.Lines.Sum(x => x.VatPln));
        Assert.Equal(result.Vat, result.TaxSummaries.Sum(x => x.Vat));
        Assert.All(result.Lines, x => Assert.Equal(x.Net + x.Vat, x.Gross));
    }
    private sealed class InvoiceResultComparer : IEqualityComparer<InvoiceCalculationResult>
    {
        public static readonly InvoiceResultComparer Instance = new();
        public bool Equals(InvoiceCalculationResult? a, InvoiceCalculationResult? b) => a!.Net == b!.Net && a.Vat == b.Vat && a.Gross == b.Gross && a.Lines.SequenceEqual(b.Lines) && a.TaxSummaries.SequenceEqual(b.TaxSummaries);
        public int GetHashCode(InvoiceCalculationResult obj) => 0;
    }
}
