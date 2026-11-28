using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.SalesInvoices;

namespace xKsef.Application.SalesInvoices;

public static partial class SalesInvoiceContractMapper
{
    private static QualificationDecisionData Decision(QualificationDecisionDto x)
    {
        if (x is null) throw Invalid();
        return new() { Value = (QualificationValue)x.Value, SuggestedValue = (QualificationValue?)x.SuggestedValue,
            Source = x.Confirm ? QualificationSource.UserConfirmed : x.Value != 0 || x.SuggestedValue is not null ? QualificationSource.UserProposed : QualificationSource.Unresolved, Reason = x.Reason };
    }
    private static QualificationDecisionDto Decision(QualificationDecisionData x) => new((int)x.Value, (int?)x.SuggestedValue, x.Source == QualificationSource.UserConfirmed, x.Reason);
    private static QualificationDecisionStateDto State(string path, QualificationDecisionData x) => new(path, (int)x.Value, (int?)x.SuggestedValue, (int)x.Source, x.Reason, x.ProposedBy, x.ProposedAt, x.ConfirmedBy, x.ConfirmedAt, x.RuleCode, x.RuleVersion, x.InputFingerprint);
    private static InvoiceParticipantData Participant(InvoiceParticipantDto x) => new() { Id = x.Id, Role = (InvoiceParticipantRole?)x.Role, OtherRoleDescription = x.OtherRoleDescription, SharePercent = x.SharePercent?.Value, Party = Party(x.Party) ?? throw Invalid() };
    private static InvoiceParticipantDto Participant(InvoiceParticipantData x) => new() { Id = x.Id, Role = (int?)x.Role, OtherRoleDescription = x.OtherRoleDescription, SharePercent = Exact(x.SharePercent), Party = Party(x.Party)! };
    private static InvoiceTaxTreatmentData? TaxTreatment(InvoiceTaxTreatmentDto? x) => x is null ? null : new() { Version = x.Version, Kind = (InvoiceTaxTreatmentKind?)x.Kind, DomesticRateCode = x.DomesticRateCode, ForeignVatRate = x.ForeignVatRate?.Value, TaxCountryCode = x.TaxCountryCode };
    private static InvoiceTaxTreatmentDto? TaxTreatment(InvoiceTaxTreatmentData? x) => x is null ? null : new(x.Version, (int?)x.Kind, x.DomesticRateCode, Exact(x.ForeignVatRate), x.TaxCountryCode);
    private static InvoiceExemptionBasisData? Basis(InvoiceExemptionBasisDto? x) => x is null ? null : new() { Version = x.Version, Kind = (ExemptionBasisKind?)x.Kind, Text = x.Text };
    private static InvoiceExemptionBasisDto? Basis(InvoiceExemptionBasisData? x) => x is null ? null : new(x.Version, (int?)x.Kind, x.Text);
    private static SalesInvoiceFa3Data Fa3(SalesInvoiceFa3Dto x) => new()
    {
        Version = x.Version, WarehouseDocumentNumbers = x.WarehouseDocumentNumbers.ToArray(),
        AuthorizedParty = x.AuthorizedParty is not { } a ? null : new((AuthorizedPartyRole?)a.Role, Party(a.Party) ?? throw Invalid()),
        Qualifications = new()
        {
            CashAccounting = Decision(x.Qualifications.CashAccounting),
            SelfBilling = Decision(x.Qualifications.SelfBilling),
            ReverseCharge = Decision(x.Qualifications.ReverseCharge),
            SplitPayment = Decision(x.Qualifications.SplitPayment),
            HasExemptSales = Decision(x.Qualifications.HasExemptSales),
            HasMarginSales = Decision(x.Qualifications.HasMarginSales),
            HasNewMeansOfTransport = Decision(x.Qualifications.HasNewMeansOfTransport),
            Article42Paragraph5Applies = Decision(x.Qualifications.Article42Paragraph5Applies),
            TriangularTransaction = Decision(x.Qualifications.TriangularTransaction),
            ConcernsJstUnit = Decision(x.Qualifications.ConcernsJstUnit),
            ConcernsVatGroupMember = Decision(x.Qualifications.ConcernsVatGroupMember),
            InvoiceForReceipt = Decision(x.Qualifications.InvoiceForReceipt),
            RelatedParties = Decision(x.Qualifications.RelatedParties),
            ExciseRefund = Decision(x.Qualifications.ExciseRefund),
        }
    };
    private static SalesInvoiceFa3Dto Fa3(SalesInvoiceFa3Data x) => new()
    {
        Version = x.Version, WarehouseDocumentNumbers = x.WarehouseDocumentNumbers.ToArray(),
        AuthorizedParty = x.AuthorizedParty is not { } a ? null : new((int?)a.Role, Party(a.Party)!),
        Qualifications = new()
        {
            CashAccounting = Decision(x.Qualifications.CashAccounting),
            SelfBilling = Decision(x.Qualifications.SelfBilling),
            ReverseCharge = Decision(x.Qualifications.ReverseCharge),
            SplitPayment = Decision(x.Qualifications.SplitPayment),
            HasExemptSales = Decision(x.Qualifications.HasExemptSales),
            HasMarginSales = Decision(x.Qualifications.HasMarginSales),
            HasNewMeansOfTransport = Decision(x.Qualifications.HasNewMeansOfTransport),
            Article42Paragraph5Applies = Decision(x.Qualifications.Article42Paragraph5Applies),
            TriangularTransaction = Decision(x.Qualifications.TriangularTransaction),
            ConcernsJstUnit = Decision(x.Qualifications.ConcernsJstUnit),
            ConcernsVatGroupMember = Decision(x.Qualifications.ConcernsVatGroupMember),
            InvoiceForReceipt = Decision(x.Qualifications.InvoiceForReceipt),
            RelatedParties = Decision(x.Qualifications.RelatedParties),
            ExciseRefund = Decision(x.Qualifications.ExciseRefund),
        }
    };
}
