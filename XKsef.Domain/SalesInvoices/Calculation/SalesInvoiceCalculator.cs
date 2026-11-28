namespace xKsef.Domain.SalesInvoices.Calculation;

public sealed class SalesInvoiceCalculator
{
    public static bool Supports(VatCalculationMethod method) => method is VatCalculationMethod.PerLineNet or VatCalculationMethod.PerLineGross or VatCalculationMethod.ByNetTotals or VatCalculationMethod.ByGrossTotals;
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
        if (draft.Fa3.Qualifications.HasMarginSales.Value == QualificationValue.Yes)
            missing.Add(new("fa3.qualifications.HasMarginSales", "MarginCalculationNotImplemented", "Procedura marży wymaga odrębnego kalkulatora."));
        if (!Supports(draft.VatMethod)) missing.Add(new("vatMethod", "VatMethodNotImplemented", "Wybrana metoda VAT nie ma jeszcze zaimplementowanego algorytmu."));
        if (draft.Lines.Count == 0) missing.Add(new("lines", "RequiredForCalculation", "Dodaj pozycję faktury."));
        foreach (var line in draft.Lines)
        {
            if (line.TaxTreatment is { } tax && !MatchesLegacyTreatment(tax, line.TaxCode))
                missing.Add(new($"lines[{line.Id}].taxTreatment", tax.Kind is InvoiceTaxTreatmentKind.Margin or InvoiceTaxTreatmentKind.UnionOss ? "TaxTreatmentCalculationNotImplemented" : "TaxTreatmentUnresolvedOrConflicting", "Kwalifikacja jest nieustalona, sprzeczna z kodem VAT albo wymaga niezaimplementowanego algorytmu."));
            if (line.Quantity is null) missing.Add(new($"lines[{line.Id}].quantity", "RequiredForCalculation", "Podaj ilość."));
            if (line.UnitPrice is null) missing.Add(new($"lines[{line.Id}].unitPrice", "RequiredForCalculation", "Podaj cenę jednostkową."));
        }
        if (draft.Currency != "PLN" && draft.ExchangeRates.Count == 0) missing.Add(new("exchangeRates", "RequiredForCalculation", "Podaj kurs waluty do PLN."));
        return missing;
    }

    // This only checks compatibility with existing TaxCode arithmetic. It does not infer tax treatment.
    private static bool MatchesLegacyTreatment(InvoiceTaxTreatmentData tax, string taxCode)
    {
        if (tax.ForeignVatRate is not null) return false;
        return tax.Kind switch
        {
            InvoiceTaxTreatmentKind.DomesticTaxable => tax.DomesticRateCode == taxCode && tax.DomesticRateCode is not null && TaxRates.ContainsKey(tax.DomesticRateCode) && tax.TaxCountryCode is null or "PL",
            InvoiceTaxTreatmentKind.DomesticZero => taxCode == "0 KR" && tax.DomesticRateCode is null,
            InvoiceTaxTreatmentKind.IntraCommunitySupplyZero => taxCode == "0 WDT" && tax.DomesticRateCode is null,
            InvoiceTaxTreatmentKind.ExportZero => taxCode == "0 EX" && tax.DomesticRateCode is null,
            InvoiceTaxTreatmentKind.Exempt => taxCode == "zw" && tax.DomesticRateCode is null,
            InvoiceTaxTreatmentKind.OutsidePolandOther => taxCode == "np I" && tax.DomesticRateCode is null,
            InvoiceTaxTreatmentKind.OutsidePolandArticle100Services => taxCode == "np II" && tax.DomesticRateCode is null,
            InvoiceTaxTreatmentKind.DomesticReverseCharge => taxCode == "oo" && tax.DomesticRateCode is null,
            _ => false
        };
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
            return new WorkingLine(line, discount, line.PriceBasis == PriceBasis.Net ? value : FinancialMath.Ratio(value, 1m, factor),
                line.PriceBasis == PriceBasis.Gross ? value : FinancialMath.Ratio(value, factor, 1m));
        }).ToArray();

        decimal globalAmount = 0m;
        if (draft.GlobalDiscount is { Value: > 0 } global)
        {
            var weights = work.ToDictionary(x => x.Input.Id, x => draft.GlobalDiscountBasis == PriceBasis.Net ? x.Net : x.Gross);
            var total = Sum(weights.Values);
            var exactDiscount = global.Kind == DiscountKind.Percent ? FinancialMath.Ratio(total, global.Value, 100m) : global.Value;
            if (exactDiscount > total) throw new SalesInvoiceValidationException([new("globalDiscount", "ExceedsBase", "Rabat przekracza wartość podstawy.")]);
            globalAmount = FinancialMath.RoundAmount(exactDiscount);
            if (globalAmount > 0 || global.Kind == DiscountKind.Percent && global.Value == 100m)
            {
                var moneyShares = FinancialMath.Allocate(globalAmount, weights);
                // Monetary document discounts are allocated in cents where representable.
                // Sub-cent bases require proportional intermediates; never make a base negative.
                var exactShares = global.Kind == DiscountKind.Percent && global.Value == 100m ? weights
                    : moneyShares.All(x => x.Value <= weights[x.Key]) ? moneyShares : FinancialMath.AllocateProportion(exactDiscount, weights);
                var displayShares = FinancialMath.AllocateRounding(globalAmount, exactShares);
                foreach (var line in work)
                {
                    var after = weights[line.Input.Id] - exactShares[line.Input.Id];
                    var factor = 1m + TaxRates[line.Input.TaxCode] / 100m;
                    line.Net = draft.GlobalDiscountBasis == PriceBasis.Net ? after : FinancialMath.Ratio(after, 1m, factor);
                    line.Gross = draft.GlobalDiscountBasis == PriceBasis.Gross ? after : FinancialMath.Ratio(after, factor, 1m);
                    line.GlobalDiscount = displayShares[line.Input.Id];
                }
            }
        }
        var summaries = new List<CalculatedTaxSummary>();
        var netBasis = draft.VatMethod is VatCalculationMethod.PerLineNet or VatCalculationMethod.ByNetTotals;
        var aggregate = draft.VatMethod is VatCalculationMethod.ByNetTotals or VatCalculationMethod.ByGrossTotals;
        foreach (var group in work.GroupBy(x => x.Input.TaxCode, StringComparer.Ordinal).OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            var rate = TaxRates[group.Key];
            var denominator = netBasis ? 100m : 100m + rate;
            var bases = group.ToDictionary(x => x.Input.Id, x => netBasis ? x.Net : x.Gross);
            decimal legalVat;
            decimal calculationBase;
            if (!aggregate)
            {
                foreach (var line in group)
                {
                    var moneyBase = FinancialMath.RoundAmount(bases[line.Input.Id]);
                    line.Vat = FinancialMath.RoundRatio(moneyBase, rate, denominator);
                    line.FinalNet = netBasis ? moneyBase : moneyBase - line.Vat;
                    line.FinalGross = netBasis ? moneyBase + line.Vat : moneyBase;
                }
                calculationBase = Sum(group.Select(x => netBasis ? x.FinalNet : x.FinalGross));
                legalVat = Sum(group.Select(x => x.Vat));
            }
            else
            {
                // Sum at full precision, then round the group's base BEFORE applying the VAT formula.
                calculationBase = FinancialMath.RoundAmount(Sum(bases.Values));
                legalVat = FinancialMath.RoundRatio(calculationBase, rate, denominator);
                var allocatedBases = FinancialMath.AllocateRounding(calculationBase, bases);
                // Allocate against monetary bases whose sum equals calculationBase, keeping group VAT authoritative.
                var allocatedVat = FinancialMath.AllocateRatio(legalVat, allocatedBases, rate, denominator);
                foreach (var line in group)
                {
                    var id = line.Input.Id;
                    line.Vat = allocatedVat[id];
                    line.FinalNet = netBasis ? allocatedBases[id] : allocatedBases[id] - line.Vat;
                    line.FinalGross = netBasis ? allocatedBases[id] + line.Vat : allocatedBases[id];
                    line.Adjustment = line.Vat - FinancialMath.RoundRatio(FinancialMath.RoundAmount(bases[id]), rate, denominator);
                    line.BaseAdjustment = allocatedBases[id] - FinancialMath.RoundAmount(bases[id]);
                }
            }
            if (group.Any(x => x.FinalNet < 0 || x.FinalGross < 0) || Sum(group.Select(x => x.Vat)) != legalVat) throw new OverflowException("Niespójna alokacja kwot.");
            var exchange = draft.ExchangeRates.SingleOrDefault();
            var vatPln = draft.Currency == "PLN" ? legalVat : FinancialMath.RoundRatio(legalVat, exchange!.Rate, exchange.QuotedUnits);
            var plnBases = group.ToDictionary(x => x.Input.Id, x => x.Vat);
            var plnAllocation = draft.Currency == "PLN" ? plnBases : FinancialMath.AllocateRatio(vatPln, plnBases, exchange!.Rate, exchange.QuotedUnits);
            foreach (var line in group) line.VatPln = plnAllocation[line.Input.Id];
            summaries.Add(new(group.Key, Sum(group.Select(x => x.FinalNet)), legalVat, Sum(group.Select(x => x.FinalGross)), vatPln,
                new(1, draft.VatMethod, netBasis ? PriceBasis.Net : PriceBasis.Gross, calculationBase, rate)));
        }

        var lines = work.Select(x => new CalculatedInvoiceLine(x.Input.Id, x.Discount, x.GlobalDiscount, draft.GlobalDiscountBasis,
            x.FinalNet, x.Vat, x.FinalGross, x.VatPln, x.Adjustment,
            aggregate ? VatAmountOrigin.TaxGroupAllocation : VatAmountOrigin.PerLineCalculation, x.BaseAdjustment)).ToArray();
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
        public decimal BaseAdjustment { get; set; }
    }
}
