using System.Text.Json.Serialization;

namespace xKsef.Contracts.SalesInvoices;

public sealed record InvoiceSalePeriodDto(DateOnly? From, DateOnly? To);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record QualificationDecisionDto(int Value = 0, int? SuggestedValue = null, bool Confirm = false, string? Reason = null);
public sealed record QualificationDecisionStateDto(string Path, int Value, int? SuggestedValue, int Source, string? Reason,
    string? ProposedBy, DateTimeOffset? ProposedAt, string? ConfirmedBy, DateTimeOffset? ConfirmedAt,
    string? RuleCode = null, string? RuleVersion = null, string? InputFingerprint = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InvoiceQualificationsDto
{
    public QualificationDecisionDto CashAccounting { get; init; } = new();
    public QualificationDecisionDto SelfBilling { get; init; } = new();
    public QualificationDecisionDto ReverseCharge { get; init; } = new();
    public QualificationDecisionDto SplitPayment { get; init; } = new();
    public QualificationDecisionDto HasExemptSales { get; init; } = new();
    public QualificationDecisionDto HasMarginSales { get; init; } = new();
    public QualificationDecisionDto HasNewMeansOfTransport { get; init; } = new();
    public QualificationDecisionDto Article42Paragraph5Applies { get; init; } = new();
    public QualificationDecisionDto TriangularTransaction { get; init; } = new();
    public QualificationDecisionDto ConcernsJstUnit { get; init; } = new();
    public QualificationDecisionDto ConcernsVatGroupMember { get; init; } = new();
    public QualificationDecisionDto InvoiceForReceipt { get; init; } = new();
    public QualificationDecisionDto RelatedParties { get; init; } = new();
    public QualificationDecisionDto ExciseRefund { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InvoiceTaxTreatmentDto(int Version = 1, int? Kind = null, string? DomesticRateCode = null, ExactDecimal? ForeignVatRate = null, string? TaxCountryCode = null);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InvoiceExemptionBasisDto(int Version = 1, int? Kind = null, string? Text = null);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InvoiceParticipantDto
{
    public Guid Id { get; init; }
    public int? Role { get; init; }
    public string? OtherRoleDescription { get; init; }
    public ExactDecimal? SharePercent { get; init; }
    public InvoicePartyDto Party { get; init; } = new();
}

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InvoiceAuthorizedPartyDto(int? Role, InvoicePartyDto Party);

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalesInvoiceFa3Dto
{
    public int Version { get; init; } = 1;
    public IReadOnlyList<string> WarehouseDocumentNumbers { get; init; } = [];
    public InvoiceQualificationsDto Qualifications { get; init; } = new();
    public InvoiceAuthorizedPartyDto? AuthorizedParty { get; init; }
}
