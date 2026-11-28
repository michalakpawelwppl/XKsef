using System.Text.Json;

namespace xKsef.Domain.SalesInvoices;

internal static class SalesInvoiceFa3Audit
{
    internal static SalesInvoiceDraft Stamp(SalesInvoiceDraft draft, SalesInvoiceDraft? previous, string actor, DateTimeOffset now)
    {
        var changed = previous is not null && JsonSerializer.Serialize(WithoutDecisions(draft)) != JsonSerializer.Serialize(WithoutDecisions(previous));
        QualificationDecisionData Decision(QualificationDecisionData input, QualificationDecisionData? old)
        {
            if (changed && old?.Source == QualificationSource.UserConfirmed) input = input with { Source = QualificationSource.UserProposed };
            return input.Stamp(old, actor, now);
        }
        SalesInvoicePartyData? Party(SalesInvoicePartyData? input, SalesInvoicePartyData? old) => input is null ? null : input with { PlPrefix = Decision(input.PlPrefix, old?.PlPrefix) };
        var qualifications = draft.Fa3.Qualifications;
        if (changed)
        {
            // Invalidate only previously confirmed decisions; no automatic tax qualification engine runs here.
            qualifications = Invalidate(qualifications, previous!.Fa3.Qualifications);
        }
        return draft with
        {
            Seller = Party(draft.Seller, previous?.Seller), Buyer = Party(draft.Buyer, previous?.Buyer),
            Lines = draft.Lines.Select(x => x with { Annex15 = Decision(x.Annex15, previous?.Lines.SingleOrDefault(p => p.Id == x.Id)?.Annex15) }).ToArray(),
            Participants = draft.Participants.Select(x => x with { Party = Party(x.Party, previous?.Participants.SingleOrDefault(p => p.Id == x.Id)?.Party)! }).ToArray(),
            Fa3 = draft.Fa3 with
            {
                Qualifications = qualifications.Stamp(previous?.Fa3.Qualifications ?? new(), actor, now),
                AuthorizedParty = draft.Fa3.AuthorizedParty is not { } authorized ? null : authorized with { Party = Party(authorized.Party, previous?.Fa3.AuthorizedParty?.Party)! }
            }
        };
    }

    private static SalesInvoiceDraft WithoutDecisions(SalesInvoiceDraft draft)
    {
        SalesInvoicePartyData? Party(SalesInvoicePartyData? party) => party is null ? null : party with { PlPrefix = new() };
        return draft with
        {
            ModelVersion = 1, Seller = Party(draft.Seller), Buyer = Party(draft.Buyer),
            Lines = draft.Lines.Select(x => x with { Annex15 = new() }).ToArray(),
            Participants = draft.Participants.Select(x => x with { Party = Party(x.Party)! }).ToArray(),
            Fa3 = draft.Fa3 with { Qualifications = new(), AuthorizedParty = draft.Fa3.AuthorizedParty is not { } authorized ? null : authorized with { Party = Party(authorized.Party)! } }
        };
    }

    private static InvoiceQualificationsData Invalidate(InvoiceQualificationsData input, InvoiceQualificationsData previous) => input with
    {
        // Generated assignments remain explicit to keep the set of audited decisions reviewable.
        CashAccounting = Reset(input.CashAccounting, previous.CashAccounting),
        SelfBilling = Reset(input.SelfBilling, previous.SelfBilling),
        ReverseCharge = Reset(input.ReverseCharge, previous.ReverseCharge),
        SplitPayment = Reset(input.SplitPayment, previous.SplitPayment),
        HasExemptSales = Reset(input.HasExemptSales, previous.HasExemptSales),
        HasMarginSales = Reset(input.HasMarginSales, previous.HasMarginSales),
        HasNewMeansOfTransport = Reset(input.HasNewMeansOfTransport, previous.HasNewMeansOfTransport),
        Article42Paragraph5Applies = Reset(input.Article42Paragraph5Applies, previous.Article42Paragraph5Applies),
        TriangularTransaction = Reset(input.TriangularTransaction, previous.TriangularTransaction),
        ConcernsJstUnit = Reset(input.ConcernsJstUnit, previous.ConcernsJstUnit),
        ConcernsVatGroupMember = Reset(input.ConcernsVatGroupMember, previous.ConcernsVatGroupMember),
        InvoiceForReceipt = Reset(input.InvoiceForReceipt, previous.InvoiceForReceipt),
        RelatedParties = Reset(input.RelatedParties, previous.RelatedParties),
        ExciseRefund = Reset(input.ExciseRefund, previous.ExciseRefund)
    };

    private static QualificationDecisionData Reset(QualificationDecisionData input, QualificationDecisionData previous) => previous.Source == QualificationSource.UserConfirmed ? input with { Source = QualificationSource.UserProposed } : input;
}
