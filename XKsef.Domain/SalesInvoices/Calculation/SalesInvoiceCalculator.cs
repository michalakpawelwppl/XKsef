namespace xKsef.Domain.SalesInvoices.Calculation;

public sealed class SalesInvoiceCalculator
{
    public static readonly IReadOnlyDictionary<string, decimal> TaxRates = new Dictionary<string, decimal>(StringComparer.Ordinal)
    {
        ["23"] = 23m, ["22"] = 22m, ["8"] = 8m, ["7"] = 7m, ["5"] = 5m, ["4"] = 4m, ["3"] = 3m,
        ["0 KR"] = 0m, ["0 WDT"] = 0m, ["0 EX"] = 0m, ["zw"] = 0m, ["np I"] = 0m, ["np II"] = 0m, ["oo"] = 0m
    };

    public InvoiceCalculationResult Calculate(SalesInvoiceDraft input)
    {
        SalesInvoiceValidator.Validate(input);
        var draft = input.Normalize();
        try { return CalculateCore(draft); }
        catch (OverflowException) { throw new SalesInvoiceValidationException([new("lines", "ArithmeticRange", "Dane przekraczają bezpieczną precyzję lub zakres obliczeń decimal.")]); }
    }

    public static IReadOnlyList<InvoiceValidationIssue> GetMissingData(SalesInvoiceDraft draft)
    {
        var missing = new List<InvoiceValidationIssue>();
        if (draft.Lines.Count == 0) missing.Add(new("lines", "RequiredForCalculation", "Dodaj pozycję faktury."));
        foreach (var line in draft.Lines)
        {
            if (line.Quantity is null) missing.Add(new($"lines[{line.Id}].quantity", "RequiredForCalculation", "Podaj ilość."));
            if (line.UnitPrice is null) missing.Add(new($"lines[{line.Id}].unitPrice", "RequiredForCalculation", "Podaj cenę jednostkową."));
        }
        if (draft.Currency != "PLN" && draft.ExchangeRates.Count == 0) missing.Add(new("exchangeRates", "RequiredForCalculation", "Podaj kurs waluty do PLN."));
        return missing;
    }

    private static InvoiceCalculationResult CalculateCore(SalesInvoiceDraft draft)
    {
        var missing = GetMissingData(draft);
        if (missing.Count != 0) return new(false, missing, [], [], null, null, null, null, 0m);

        var work = draft.Lines.OrderBy(x => x.Id.ToString("N"), StringComparer.Ordinal).Select(line =>
        {
            var amount = FinancialMath.Multiply(line.Quantity!.Value, line.UnitPrice!.Value);
            var discount = DiscountAmount(line.Discount, amount);
            var value = amount - discount;
            var factor = 1m + TaxRates[line.TaxCode] / 100m;
            return new WorkingLine(line, discount, line.PriceBasis == PriceBasis.Net ? value : value / factor,
                line.PriceBasis == PriceBasis.Gross ? value : FinancialMath.Multiply(value, factor));
        }).ToArray();

        decimal globalAmount = 0m;
        if (draft.GlobalDiscount is { Value: > 0 })
        {
            var weights = work.ToDictionary(x => x.Input.Id, x => FinancialMath.RoundAmount(draft.GlobalDiscountBasis == PriceBasis.Net ? x.Net : x.Gross));
            var total = Sum(weights.Values);
            globalAmount = FinancialMath.RoundAmount(DiscountAmount(draft.GlobalDiscount, total));
            var allocations = FinancialMath.Allocate(globalAmount, weights);
            foreach (var line in work)
            {
                if (globalAmount == 0m) continue;
                var id = line.Input.Id;
                line.GlobalDiscount = allocations[id];
                var after = weights[id] - line.GlobalDiscount;
                var factor = 1m + TaxRates[line.Input.TaxCode] / 100m;
                // The declared global-discount basis establishes monetary values before VAT calculation.
                line.Net = draft.GlobalDiscountBasis == PriceBasis.Net ? after : after / factor;
                line.Gross = draft.GlobalDiscountBasis == PriceBasis.Gross ? after : FinancialMath.Multiply(after, factor);
            }
        }

        foreach (var group in work.GroupBy(x => x.Input.TaxCode, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var rate = TaxRates[group.Key];
            if (draft.VatMethod == VatCalculationMethod.PerLine)
            {
                foreach (var line in group)
                {
                    // Preserve the money basis of an applied global discount. Converting it back to
                    // each source-price basis would consume/restore a cent and lose the declared discount.
                    var effectiveBasis = globalAmount > 0m ? draft.GlobalDiscountBasis : line.Input.PriceBasis;
                    if (effectiveBasis == PriceBasis.Net)
                    {
                        line.FinalNet = FinancialMath.RoundAmount(line.Net);
                        line.Vat = FinancialMath.RoundAmount(FinancialMath.Multiply(line.FinalNet, rate) / 100m);
                        line.FinalGross = checked(line.FinalNet + line.Vat);
                    }
                    else
                    {
                        line.FinalGross = FinancialMath.RoundAmount(line.Gross);
                        line.Vat = FinancialMath.RoundAmount(FinancialMath.Multiply(line.FinalGross, rate) / (100m + rate));
                        line.FinalNet = line.FinalGross - line.Vat;
                    }
                }
            }
            else
            {
                var netBasis = draft.VatMethod == VatCalculationMethod.ByNetTotals;
                var bases = group.ToDictionary(x => x.Input.Id, x => FinancialMath.RoundAmount(netBasis ? x.Net : x.Gross));
                var total = Sum(bases.Values);
                var tax = FinancialMath.RoundAmount(FinancialMath.Multiply(total, rate) / (netBasis ? 100m : 100m + rate));
                var unrounded = bases.ToDictionary(x => x.Key, x => FinancialMath.Multiply(x.Value, rate) / (netBasis ? 100m : 100m + rate));
                var allocated = FinancialMath.AllocateRounding(tax, unrounded);
                foreach (var line in group)
                {
                    line.Vat = allocated[line.Input.Id];
                    line.FinalNet = netBasis ? bases[line.Input.Id] : bases[line.Input.Id] - line.Vat;
                    line.FinalGross = netBasis ? bases[line.Input.Id] + line.Vat : bases[line.Input.Id];
                    line.Adjustment = line.Vat - FinancialMath.RoundAmount(FinancialMath.Multiply(bases[line.Input.Id], rate) / (netBasis ? 100m : 100m + rate));
                }
            }
            var groupVat = Sum(group.Select(x => x.Vat));
            var exchange = draft.ExchangeRates.SingleOrDefault();
            var vatPln = draft.Currency == "PLN" ? groupVat : FinancialMath.RoundAmount(FinancialMath.Multiply(groupVat, exchange!.Rate) / exchange.QuotedUnits);
            var plnValues = group.ToDictionary(x => x.Input.Id, x => draft.Currency == "PLN" ? x.Vat : FinancialMath.Multiply(x.Vat, exchange!.Rate) / exchange.QuotedUnits);
            var plnAllocation = FinancialMath.AllocateRounding(vatPln, plnValues);
            foreach (var line in group) line.VatPln = plnAllocation[line.Input.Id];
        }

        var lines = work.Select(x => new CalculatedInvoiceLine(x.Input.Id, x.Discount, x.GlobalDiscount, draft.GlobalDiscountBasis, x.FinalNet, x.Vat, x.FinalGross, x.VatPln, x.Adjustment)).ToArray();
        var summaries = work.GroupBy(x => x.Input.TaxCode, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal)
            .Select(g => new CalculatedTaxSummary(g.Key, Sum(g.Select(x => x.FinalNet)), Sum(g.Select(x => x.Vat)), Sum(g.Select(x => x.FinalGross)), Sum(g.Select(x => x.VatPln)))).ToArray();
        var net = Sum(lines.Select(x => x.Net)); var vat = Sum(lines.Select(x => x.Vat)); var gross = Sum(lines.Select(x => x.Gross)); var pln = Sum(lines.Select(x => x.VatPln));
        if (new[] { net, vat, gross, pln }.Concat(lines.SelectMany(x => new[] { x.Net, x.Vat, x.Gross, x.VatPln })).Any(x => x > FinancialMath.MaxAmount)) throw new OverflowException();
        return new(true, [], lines, summaries, net, vat, gross, pln, globalAmount);
    }

    private static decimal Sum(IEnumerable<decimal> values) => values.Aggregate(0m, (a, b) => checked(a + b));
    private static decimal DiscountAmount(InvoiceDiscount? discount, decimal amount)
    {
        var value = discount is null ? 0m : discount.Kind == DiscountKind.Amount ? discount.Value : FinancialMath.Multiply(amount, discount.Value) / 100m;
        if (value > amount) throw new SalesInvoiceValidationException([new("discount", "ExceedsBase", "Rabat przekracza wartość podstawy.")]);
        return value;
    }

    private sealed class WorkingLine(SalesInvoiceLineInput input, decimal discount, decimal net, decimal gross)
    {
        public SalesInvoiceLineInput Input { get; } = input;
        public decimal Discount { get; } = discount;
        public decimal Net { get; set; } = net;
        public decimal Gross { get; set; } = gross;
        public decimal GlobalDiscount { get; set; }
        public decimal FinalNet { get; set; }
        public decimal Vat { get; set; }
        public decimal FinalGross { get; set; }
        public decimal VatPln { get; set; }
        public decimal Adjustment { get; set; }
    }
}
