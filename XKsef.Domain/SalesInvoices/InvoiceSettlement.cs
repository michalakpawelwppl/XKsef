using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Domain.SalesInvoices;

public sealed record InvoiceSettlementEntry(decimal? Amount = null, string? Reason = null);
public sealed record InvoiceSettlementData
{
    public IReadOnlyList<InvoiceSettlementEntry> Charges { get; init; } = [];
    public IReadOnlyList<InvoiceSettlementEntry> Deductions { get; init; } = [];
    internal InvoiceSettlementData Freeze() => this with { Charges = Array.AsReadOnly(Charges.ToArray()), Deductions = Array.AsReadOnly(Deductions.ToArray()) };
    internal InvoiceSettlementData Copy() => this with { Charges = Charges.ToArray(), Deductions = Deductions.ToArray() };
}
public sealed record InvoiceSettlementAmounts(decimal? ChargesTotal, decimal? DeductionsTotal, decimal? AmountDue, decimal? Overpayment);

public static class InvoiceSettlementCalculator
{
    // FA(3) TKwotowy permits signed decimal amounts with at most two fractional digits.
    public static void ValidateInput(InvoiceSettlementData? input, ICollection<InvoiceValidationIssue> issues)
    {
        if (input is null) return;
        Entries(input.Charges, "charges"); Entries(input.Deductions, "deductions");
        void Entries(IReadOnlyList<InvoiceSettlementEntry>? entries, string name)
        {
            var path = "details.settlement." + name;
            if (entries is null || entries.Count > 100) issues.Add(new(path, "SettlementCollectionInvalid", "Podaj kolekcję zawierającą maksymalnie 100 pozycji rozliczenia."));
            if (entries is null) return;
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i]; var field = $"{path}[{i}]";
                if (entry is null) { issues.Add(new(field, "SettlementEntryRequired", "Pozycja rozliczenia nie może być null.")); continue; }
                if (entry.Amount is { } amount && (amount < -FinancialMath.MaxAmount || amount > FinancialMath.MaxAmount || !FinancialMath.HasScale(amount, 2)))
                    issues.Add(new(field + ".amount", "SettlementAmountInvalid", "Kwota musi odpowiadać TKwotowy FA(3): do 16 cyfr całkowitych i dwóch miejsc po przecinku."));
                if (entry.Reason is not { } reason) continue;
                if (Regex.Replace(reason, "[ \\t\\r\\n]+", " ").Trim(' ').EnumerateRunes().Count() > 256)
                    issues.Add(new(field + ".reason", "SettlementReasonTooLong", "Powód może zawierać maksymalnie 256 znaków po normalizacji białych znaków XML."));
                try { XmlConvert.VerifyXmlChars(reason); }
                catch (XmlException) { issues.Add(new(field + ".reason", "InvalidXmlCharacters", "Powód zawiera znaki niedozwolone w XML.")); }
            }
        }
    }

    public static InvoiceSettlementAmounts? Calculate(InvoiceSettlementData? input, decimal? invoiceGross)
    {
        if (input is null) return null;
        var issues = new List<InvoiceValidationIssue>(); ValidateInput(input, issues);
        if (invoiceGross is { } gross && !FinancialMath.HasScale(gross, 2))
            issues.Add(new("settlementAmounts", "SettlementInvoiceAmountInvalid", "Kwota faktury używana w rozliczeniu musi być zaokrąglona do dwóch miejsc po przecinku."));
        if (issues.Count != 0) throw new SalesInvoiceValidationException(issues);
        decimal? Sum(IReadOnlyList<InvoiceSettlementEntry> entries) => entries.Any(x => x.Amount is null) ? null : entries.Sum(x => x.Amount!.Value);
        var charges = Sum(input.Charges); var deductions = Sum(input.Deductions);
        var balance = invoiceGross + charges - deductions;
        foreach (var value in new[] { charges, deductions, balance })
            if (value is < -FinancialMath.MaxAmount or > FinancialMath.MaxAmount)
                throw new SalesInvoiceValidationException([new("details.settlement", "SettlementArithmeticRange", "Suma lub wynik rozliczenia przekracza zakres kwoty FA(3).")]);
        // Inputs and gross are already monetary amounts. Addition/subtraction needs no intermediate rounding.
        return new(charges, deductions, balance >= 0 ? balance : null, balance < 0 ? -balance : null);
    }

    public static void ValidateForIssuance(InvoiceSettlementData? input, decimal? invoiceGross, InvoiceSettlementAmounts? stored, ICollection<InvoiceValidationIssue> issues)
    {
        InvoiceSettlementAmounts? expected;
        try { expected = Calculate(input, invoiceGross); }
        catch (SalesInvoiceValidationException error) { foreach (var issue in error.Issues) issues.Add(issue); return; }
        if (expected != stored) issues.Add(new("settlementAmounts", "SettlementAmountsMismatch", "Zapisane kwoty rozliczenia nie są zgodne z kwotą faktury i pozycjami rozliczenia."));
        if (input is null) return;
        foreach (var (entries, name) in new[] { (input.Charges, "charges"), (input.Deductions, "deductions") })
            for (var i = 0; i < entries.Count; i++)
            {
                var entry = entries[i]; var field = $"details.settlement.{name}[{i}]";
                if (entry.Amount is null) issues.Add(new(field + ".amount", "SettlementAmountRequired", "Podaj kwotę pozycji rozliczenia."));
                if (entry.Reason is null || Regex.Replace(entry.Reason, "[ \\t\\r\\n]+", " ").Trim(' ').Length == 0)
                    issues.Add(new(field + ".reason", "SettlementReasonRequired", "Podaj powód pozycji rozliczenia."));
            }
        if (expected?.AmountDue is null && expected?.Overpayment is null)
            issues.Add(new("settlementAmounts", "SettlementIncomplete", "Do obliczenia wyniku rozliczenia potrzebna jest kwota faktury i komplet kwot pozycji rozliczenia."));
    }
}
