namespace xKsef.Application.SalesInvoices.Snapshots;

// Snapshot-owned contracts. Do not replace these with mutable domain types or API DTOs.
public sealed record ApprovedQualificationDecision
{
    public int? Value { get; init; }
    public int? SuggestedValue { get; init; }
    public int? Source { get; init; }
    public string? Reason { get; init; }
    public string? RuleCode { get; init; }
    public string? RuleVersion { get; init; }
    public string? InputFingerprint { get; init; }
    public string? ProposedBy { get; init; }
    public DateTimeOffset? ProposedAt { get; init; }
    public string? ConfirmedBy { get; init; }
    public DateTimeOffset? ConfirmedAt { get; init; }
}
public sealed record ApprovedQualifications
{
    public ApprovedQualificationDecision? CashAccounting { get; init; }
    public ApprovedQualificationDecision? SelfBilling { get; init; }
    public ApprovedQualificationDecision? ReverseCharge { get; init; }
    public ApprovedQualificationDecision? SplitPayment { get; init; }
    public ApprovedQualificationDecision? HasExemptSales { get; init; }
    public ApprovedQualificationDecision? HasMarginSales { get; init; }
    public ApprovedQualificationDecision? HasNewMeansOfTransport { get; init; }
    public ApprovedQualificationDecision? Article42Paragraph5Applies { get; init; }
    public ApprovedQualificationDecision? TriangularTransaction { get; init; }
    public ApprovedQualificationDecision? ConcernsJstUnit { get; init; }
    public ApprovedQualificationDecision? ConcernsVatGroupMember { get; init; }
    public ApprovedQualificationDecision? InvoiceForReceipt { get; init; }
    public ApprovedQualificationDecision? RelatedParties { get; init; }
    public ApprovedQualificationDecision? ExciseRefund { get; init; }
}
public sealed record ApprovedSalePeriod(DateOnly? From, DateOnly? To, int? Version);
public sealed record ApprovedTaxTreatment(int? Version, int? Kind, string? DomesticRateCode, SnapshotDecimal? ForeignVatRate, string? TaxCountryCode);
public sealed record ApprovedExemptionBasis(int? Version, int? Kind, string? Text);
public sealed record ApprovedParticipant(Guid? Id, int? Role, string? OtherRoleDescription, SnapshotDecimal? SharePercent, ApprovedInvoiceParty? Party, int? Ordinal = null);
public sealed record ApprovedAuthorizedParty(int? Role, ApprovedInvoiceParty? Party);
public sealed record ApprovedFa3Data(int? Version, IReadOnlyList<string>? WarehouseDocumentNumbers, ApprovedQualifications? Qualifications, ApprovedAuthorizedParty? AuthorizedParty);
