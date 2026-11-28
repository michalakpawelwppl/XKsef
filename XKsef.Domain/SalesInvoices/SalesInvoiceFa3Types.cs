using System.Text.Json.Serialization;

namespace xKsef.Domain.SalesInvoices;

public enum SaleDateMode { Unknown = 0, SameAsIssueDate = 1, CommonDate = 2, Period = 3, PerLine = 4, NotDetermined = 5 }
public enum QualificationValue { Unknown = 0, No = 1, Yes = 2 }
public enum QualificationSource { Unresolved = 0, UserProposed = 1, UserConfirmed = 2, DeterministicRule = 3 }
public enum SellerInformationStatus { Liquidation = 1, Restructuring = 2, Bankruptcy = 3, InheritedBusiness = 4 }
public enum InvoiceParticipantRole { Other = 0, Factor = 1, Recipient = 2, OriginalEntity = 3, AdditionalBuyer = 4, Issuer = 5, Payer = 6, JstIssuer = 7, JstRecipient = 8, VatGroupIssuer = 9, VatGroupRecipient = 10, Employee = 11 }
public enum AuthorizedPartyRole { EnforcementAuthority = 1, Bailiff = 2, TaxRepresentative = 3 }
public enum InvoiceTaxTreatmentKind { DomesticTaxable = 0, DomesticZero = 1, IntraCommunitySupplyZero = 2, ExportZero = 3, Exempt = 4, OutsidePolandOther = 5, OutsidePolandArticle100Services = 6, DomesticReverseCharge = 7, UnionOss = 8, Margin = 9 }
public enum ExemptionBasisKind { PolishLaw = 0, EuDirective = 1, OtherLegalBasis = 2 }
public enum InvoiceProcedureCode { WSTO_EE = 0, IED = 1, TT_D = 2, I_42 = 3, I_63 = 4, B_SPV = 5, B_SPV_DOSTAWA = 6, B_MPV_PROWIZJA = 7 }

public sealed record InvoiceSalePeriodData(DateOnly? From, DateOnly? To)
{
    public int Version { get; init; } = 1;
}

public sealed record QualificationDecisionData
{
    public QualificationValue Value { get; init; }
    public QualificationValue? SuggestedValue { get; init; }
    public QualificationSource Source { get; init; }
    public string? Reason { get; init; }
    public string? RuleCode { get; init; }
    public string? RuleVersion { get; init; }
    public string? InputFingerprint { get; init; }
    public string? ProposedBy { get; init; }
    public DateTimeOffset? ProposedAt { get; init; }
    public string? ConfirmedBy { get; init; }
    public DateTimeOffset? ConfirmedAt { get; init; }

    // All audit information is server-owned, also for internal callers of UpdateDraft.
    internal QualificationDecisionData Stamp(QualificationDecisionData? previous, string actor, DateTimeOffset now)
    {
        if (previous is not null && Value == previous.Value && SuggestedValue == previous.SuggestedValue && Source == previous.Source && Reason == previous.Reason) return previous;
        var source = Source == QualificationSource.UserConfirmed ? QualificationSource.UserConfirmed
            : Value != QualificationValue.Unknown || SuggestedValue is not null ? QualificationSource.UserProposed : QualificationSource.Unresolved;
        return this with { Source = source, RuleCode = null, RuleVersion = null, InputFingerprint = null, ProposedBy = source == QualificationSource.Unresolved ? null : actor,
            ProposedAt = source == QualificationSource.Unresolved ? null : now,
            ConfirmedBy = source == QualificationSource.UserConfirmed ? actor : null,
            ConfirmedAt = source == QualificationSource.UserConfirmed ? now : null };
    }

    [JsonIgnore]
    public bool IsEmpty => Value == QualificationValue.Unknown && SuggestedValue is null && Source == QualificationSource.Unresolved && Reason is null;
}

public sealed record InvoiceQualificationsData
{
    public QualificationDecisionData CashAccounting { get; init; } = new();
    public QualificationDecisionData SelfBilling { get; init; } = new();
    public QualificationDecisionData ReverseCharge { get; init; } = new();
    public QualificationDecisionData SplitPayment { get; init; } = new();
    public QualificationDecisionData HasExemptSales { get; init; } = new();
    public QualificationDecisionData HasMarginSales { get; init; } = new();
    public QualificationDecisionData HasNewMeansOfTransport { get; init; } = new();
    public QualificationDecisionData Article42Paragraph5Applies { get; init; } = new();
    public QualificationDecisionData TriangularTransaction { get; init; } = new();
    public QualificationDecisionData ConcernsJstUnit { get; init; } = new();
    public QualificationDecisionData ConcernsVatGroupMember { get; init; } = new();
    public QualificationDecisionData InvoiceForReceipt { get; init; } = new();
    public QualificationDecisionData RelatedParties { get; init; } = new();
    public QualificationDecisionData ExciseRefund { get; init; } = new();

    public IEnumerable<(string Name, QualificationDecisionData Decision)> Decisions()
    {
        yield return (nameof(CashAccounting), CashAccounting);
        yield return (nameof(SelfBilling), SelfBilling);
        yield return (nameof(ReverseCharge), ReverseCharge);
        yield return (nameof(SplitPayment), SplitPayment);
        yield return (nameof(HasExemptSales), HasExemptSales);
        yield return (nameof(HasMarginSales), HasMarginSales);
        yield return (nameof(HasNewMeansOfTransport), HasNewMeansOfTransport);
        yield return (nameof(Article42Paragraph5Applies), Article42Paragraph5Applies);
        yield return (nameof(TriangularTransaction), TriangularTransaction);
        yield return (nameof(ConcernsJstUnit), ConcernsJstUnit);
        yield return (nameof(ConcernsVatGroupMember), ConcernsVatGroupMember);
        yield return (nameof(InvoiceForReceipt), InvoiceForReceipt);
        yield return (nameof(RelatedParties), RelatedParties);
        yield return (nameof(ExciseRefund), ExciseRefund);
    }

    internal InvoiceQualificationsData Stamp(InvoiceQualificationsData previous, string actor, DateTimeOffset now) => this with
    {
        CashAccounting = CashAccounting.Stamp(previous.CashAccounting, actor, now),
        SelfBilling = SelfBilling.Stamp(previous.SelfBilling, actor, now),
        ReverseCharge = ReverseCharge.Stamp(previous.ReverseCharge, actor, now),
        SplitPayment = SplitPayment.Stamp(previous.SplitPayment, actor, now),
        HasExemptSales = HasExemptSales.Stamp(previous.HasExemptSales, actor, now),
        HasMarginSales = HasMarginSales.Stamp(previous.HasMarginSales, actor, now),
        HasNewMeansOfTransport = HasNewMeansOfTransport.Stamp(previous.HasNewMeansOfTransport, actor, now),
        Article42Paragraph5Applies = Article42Paragraph5Applies.Stamp(previous.Article42Paragraph5Applies, actor, now),
        TriangularTransaction = TriangularTransaction.Stamp(previous.TriangularTransaction, actor, now),
        ConcernsJstUnit = ConcernsJstUnit.Stamp(previous.ConcernsJstUnit, actor, now),
        ConcernsVatGroupMember = ConcernsVatGroupMember.Stamp(previous.ConcernsVatGroupMember, actor, now),
        InvoiceForReceipt = InvoiceForReceipt.Stamp(previous.InvoiceForReceipt, actor, now),
        RelatedParties = RelatedParties.Stamp(previous.RelatedParties, actor, now),
        ExciseRefund = ExciseRefund.Stamp(previous.ExciseRefund, actor, now),
    };
}

public sealed record InvoiceTaxTreatmentData
{
    public int Version { get; init; } = 1;
    public InvoiceTaxTreatmentKind? Kind { get; init; }
    public string? DomesticRateCode { get; init; }
    public decimal? ForeignVatRate { get; init; }
    public string? TaxCountryCode { get; init; }
}

public sealed record InvoiceExemptionBasisData
{
    public int Version { get; init; } = 1;
    public ExemptionBasisKind? Kind { get; init; }
    public string? Text { get; init; }
}

public sealed record InvoiceParticipantData
{
    public Guid Id { get; init; }
    public InvoiceParticipantRole? Role { get; init; }
    public string? OtherRoleDescription { get; init; }
    public decimal? SharePercent { get; init; }
    public SalesInvoicePartyData Party { get; init; } = new();
}

public sealed record InvoiceAuthorizedPartyData(AuthorizedPartyRole? Role, SalesInvoicePartyData Party);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalesInvoiceFa3Data
{
    [JsonRequired]
    public int Version { get; init; } = 1;
    public IReadOnlyList<string> WarehouseDocumentNumbers { get; init; } = [];
    public InvoiceQualificationsData Qualifications { get; init; } = new();
    public InvoiceAuthorizedPartyData? AuthorizedParty { get; init; }

    internal SalesInvoiceFa3Data Freeze() => this with
    {
        WarehouseDocumentNumbers = Array.AsReadOnly(WarehouseDocumentNumbers.ToArray()),
        AuthorizedParty = AuthorizedParty is null ? null : AuthorizedParty with { Party = AuthorizedParty.Party.Freeze() }
    };
}

public static class SalesInvoiceFa3Model
{
    public static bool HasExtendedData(SalesInvoiceDraft draft) =>
        draft.ModelVersion > 1 || draft.IssuePlace is not null || draft.SaleDateMode is not null || draft.SalePeriod is not null
        || draft.Participants.Count != 0 || draft.Fa3.WarehouseDocumentNumbers.Count != 0 || draft.Fa3.AuthorizedParty is not null
        || draft.Fa3.Qualifications.Decisions().Any(x => !x.Decision.IsEmpty)
        || PartyHasExtendedData(draft.Seller) || PartyHasExtendedData(draft.Buyer)
        || draft.Participants.Any(x => PartyHasExtendedData(x.Party))
        || draft.Lines.Any(x => x.SaleDate is not null || x.TaxTreatment is not null || x.ExemptionBasis is not null
            || x.Procedure is not null || x.Pkob is not null || !x.Annex15.IsEmpty || x.ExciseAmount is not null);

    public static bool PartyHasExtendedData(SalesInvoicePartyData? party) => party is not null &&
        (party.BuyerLinkKey is not null || party.CustomerNumber is not null || party.SellerStatus is not null
        || party.TaxpayerStatus != QualificationValue.Unknown || !party.PlPrefix.IsEmpty || party.IdentifierKind == TaxIdentifierKind.InternalId);
}
