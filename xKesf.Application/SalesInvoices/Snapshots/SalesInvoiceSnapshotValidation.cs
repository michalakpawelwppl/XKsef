using System.Text;
using xKsef.Domain.SalesInvoices;

namespace xKsef.Application.SalesInvoices.Snapshots;

internal static class SalesInvoiceSnapshotValidation
{
    // Validate the frozen contract and arithmetic identities only. Never run a VAT calculator on read.
    internal static void Validate(SalesInvoiceApprovalSnapshot p)
    {
        void Require([System.Diagnostics.CodeAnalysis.DoesNotReturnIf(false)] bool condition) { if (!condition) throw new SalesInvoiceApprovalSnapshotException("SnapshotIncomplete", "Snapshot nie zawiera kompletnych, spójnych danych zatwierdzonej faktury."); }
        Require(p.Input is { Lines.Count: > 0, Seller: not null, Buyer: not null, Fa3.Qualifications: not null, Participants: not null, ExchangeRates: not null, Details: not null });
        var input = p.Input!; var c = p.Calculations;
        Require(p.TenantId != Guid.Empty && p.InvoiceId != Guid.Empty && p.ApprovalId != Guid.Empty && p.SourceRevision > 0 && !string.IsNullOrWhiteSpace(p.ApprovedBy));
        Require(input.Details is { PaymentTerms: not null, BankAccounts: not null, AdditionalDescriptions: not null });
        Require(input.Details.AdditionalDescriptions.All(x => x is not null && (x.LineId is null || input.Lines!.Any(line => line is not null && line.Id == x.LineId))));
        Require(input.Lines!.All(x => x is not null) && input.Participants!.All(x => x is not null));
        Require(input.ModelVersion == p.ModelVersion && input.NumberSeriesId is not null && input.PlannedIssueDate is not null && input.Currency is not null && input.Kind == 0 && input.VatMethod is >= 0 and <= 3);
        Require(input.SaleDateMode is >= 1 and <= 5 && input.Fa3!.Version == 1 && input.Details!.Version == 1);
        Require(!string.IsNullOrWhiteSpace(input.Seller!.Name) && !string.IsNullOrWhiteSpace(input.Buyer!.Name));
        foreach (var line in input.Lines!)
        {
            Require(line.Quantity is { Value: >= 0 } && line.UnitPrice is { Value: >= 0 } && line.PriceBasis is 0 or 1 && !string.IsNullOrWhiteSpace(line.TaxCode)
                && !string.IsNullOrWhiteSpace(line.Description) && !string.IsNullOrWhiteSpace(line.Unit));
            if (line.TaxTreatment is { } tax) Require(tax.Version == 1 && tax.Kind is >= 0 and <= 7 && tax.ForeignVatRate is null);
            if (line.ExemptionBasis is { } basis) Require(basis.Version == 1 && basis.Kind is >= 0 and <= 2 && !string.IsNullOrWhiteSpace(basis.Text));
            if (line.TaxCode == "zw") Require(line.ExemptionBasis is not null);
        }
        Require(c is { IsComplete: true, Totals: not null, Lines: not null, TaxSummaries.Count: > 0 });
        Require(c!.Lines!.All(x => x is not null) && c.TaxSummaries!.All(x => x is not null));
        Require(c!.Totals!.Net is not null && c.Totals.Vat is not null && c.Totals.Gross is not null && c.Totals.VatPln is not null && c.Totals.GlobalDiscountAmount is not null);
        Require(input.Lines!.Select(x => x.Id).Distinct().Count() == input.Lines.Count && input.Lines.All(x => x.Id is { } id && id != Guid.Empty));
        Require(input.Lines.Select(x => x.Ordinal).SequenceEqual(Enumerable.Range(1, input.Lines.Count).Select(x => (int?)x)));
        Require(input.Participants!.Select(x => x.Ordinal).SequenceEqual(Enumerable.Range(1, input.Participants.Count).Select(x => (int?)x)));
        Require(c.Lines!.Select(x => x.Id).SequenceEqual(input.Lines.Select(x => x.Id)));
        var allocated = input.VatMethod is 2 or 3;
        foreach (var line in c.Lines!)
        {
            Require(line.Net is not null && line.Vat is not null && line.Gross is not null && line.VatPln is not null && line.GlobalDiscountAllocation is not null && line.VatRoundingAdjustment is not null && line.VatBaseRoundingAdjustment is not null && line.LineDiscount is { Value: >= 0 });
            Require(line.VatAmountOrigin == (allocated ? 1 : 0));
            Require(line.Net!.Value.Value + line.Vat!.Value.Value == line.Gross!.Value.Value && line.Net.Value.Value >= 0 && line.Vat.Value.Value >= 0);
        }
        foreach (var group in c.TaxSummaries!)
        {
            Require(group.Net is not null && group.Vat is not null && group.Gross is not null && group.VatPln is not null && group.CalculationDetails is not null);
            Require(group.CalculationDetails!.Method == input.VatMethod && group.CalculationDetails.Version == 1
                && group.CalculationDetails.Basis == (input.VatMethod is 0 or 2 ? 0 : 1) && group.CalculationDetails.TaxBase is not null && group.CalculationDetails.Rate is not null);
            Require(group.CalculationDetails.TaxBase!.Value.Value == (input.VatMethod is 0 or 2 ? group.Net!.Value.Value : group.Gross!.Value.Value));
            var ids = input.Lines.Where(x => x.TaxCode == group.TaxCode).Select(x => x.Id).ToHashSet();
            Require(ids.Count > 0 && group.Net!.Value.Value == c.Lines.Where(x => ids.Contains(x.Id)).Sum(x => x.Net!.Value.Value)
                && group.Vat!.Value.Value == c.Lines.Where(x => ids.Contains(x.Id)).Sum(x => x.Vat!.Value.Value)
                && group.Gross!.Value.Value == group.Net.Value.Value + group.Vat.Value.Value
                && group.VatPln!.Value.Value == c.Lines.Where(x => ids.Contains(x.Id)).Sum(x => x.VatPln!.Value.Value));
        }
        Require(c.TaxSummaries.Select(x => x.TaxCode).Distinct().Count() == c.TaxSummaries.Count
            && c.TaxSummaries.Select(x => x.TaxCode).Order().SequenceEqual(input.Lines.Select(x => x.TaxCode).Distinct().Order()));
        Require(c.Totals.Net!.Value.Value == c.Lines.Sum(x => x.Net!.Value.Value) && c.Totals.Vat!.Value.Value == c.Lines.Sum(x => x.Vat!.Value.Value)
            && c.Totals.Gross!.Value.Value == c.Lines.Sum(x => x.Gross!.Value.Value) && c.Totals.VatPln!.Value.Value == c.Lines.Sum(x => x.VatPln!.Value.Value)
            && c.Totals.GlobalDiscountAmount!.Value.Value == c.Lines.Sum(x => x.GlobalDiscountAllocation!.Value.Value));
        var qualifications = input.Fa3!.Qualifications!;
        // Explicit business requirements: adding an optional property must not silently make it mandatory on read.
        foreach (var q in new[] { qualifications.CashAccounting, qualifications.SelfBilling, qualifications.ReverseCharge, qualifications.SplitPayment,
            qualifications.HasExemptSales, qualifications.HasMarginSales, qualifications.HasNewMeansOfTransport, qualifications.TriangularTransaction,
            qualifications.ConcernsJstUnit, qualifications.ConcernsVatGroupMember, qualifications.InvoiceForReceipt, qualifications.RelatedParties, qualifications.ExciseRefund })
        {
            Require(q is not null && q.Value is >= 0 and <= 2 && q.Source is >= 0 and <= 3);
            Require(q!.Value is 1 or 2 && q.Source is 2 or 3);
            if (q.Source == 2) Require(!string.IsNullOrWhiteSpace(q.ConfirmedBy) && q.ConfirmedAt is not null);
        }
        if (qualifications.Article42Paragraph5Applies is { } article42) Require(article42.Value is >= 0 and <= 2 && article42.Source is >= 0 and <= 3);
        Require(input.Fa3!.Qualifications!.HasMarginSales!.Value == 1 && input.Fa3.Qualifications.SelfBilling!.Value == 1 && input.Fa3.Qualifications.HasNewMeansOfTransport!.Value == 1);
        Require(p.Metadata is { SourceStatus: 0, SourceRowVersion: not null, ExchangeRateHistory: not null, MissingData.Count: 0 });
        if (input.Details.Settlement is { } settlement)
        {
            Require(settlement.Charges is { Count: <= 100 } && settlement.Deductions is { Count: <= 100 });
            bool Money(SnapshotDecimal? value) => value is { } amount && amount.Value is >= -9999999999999999.99m and <= 9999999999999999.99m && decimal.Round(amount.Value, 2) == amount.Value;
            foreach (var entry in settlement.Charges.Concat(settlement.Deductions))
                Require(entry is not null && Money(entry.Amount) && entry.Reason is not null
                    && System.Text.RegularExpressions.Regex.Replace(entry.Reason, "[ \\t\\r\\n]+", " ").Trim(' ').EnumerateRunes().Count() is >= 1 and <= 256);
            var amounts = c.SettlementAmounts;
            Require(amounts?.ChargesTotal is not null && amounts.DeductionsTotal is not null);
            Require(Money(amounts.ChargesTotal) && Money(amounts.DeductionsTotal));
            Require(amounts.AmountDue is null || Money(amounts.AmountDue)); Require(amounts.Overpayment is null || Money(amounts.Overpayment));
            // Check frozen arithmetic identities without running a calculator or replacing stored results.
            Require(amounts.ChargesTotal.Value.Value == settlement.Charges.Sum(x => x.Amount!.Value.Value)
                && amounts.DeductionsTotal.Value.Value == settlement.Deductions.Sum(x => x.Amount!.Value.Value));
            var balance = c.Totals.Gross!.Value.Value + amounts.ChargesTotal.Value.Value - amounts.DeductionsTotal.Value.Value;
            Require(balance >= 0 ? amounts.AmountDue?.Value == balance && amounts.Overpayment is null
                : amounts.AmountDue is null && amounts.Overpayment?.Value == -balance);
        }
        else Require(c.SettlementAmounts is null);
        Span<byte> version = stackalloc byte[8];
        Require(Convert.TryFromBase64String(p.Metadata.SourceRowVersion, version, out var written) && written == 8);
        Require(p.Disclosure is { Version: not null, Fields.Count: > 0, InternalOnlyPaths.Count: > 0 });

    }
}
