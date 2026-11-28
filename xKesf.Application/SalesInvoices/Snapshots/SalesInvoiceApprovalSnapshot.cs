using xKsef.Domain.SalesInvoices;

namespace xKsef.Application.SalesInvoices.Snapshots;


public sealed record ApprovedSalesInvoiceCalculations(bool? IsComplete, ApprovedSalesInvoiceTotals? Totals,
    IReadOnlyList<ApprovedSalesInvoiceLineResult>? Lines, IReadOnlyList<ApprovedSalesInvoiceTaxSummary>? TaxSummaries, ApprovedInvoiceSettlementAmounts? SettlementAmounts = null);

public enum DisclosureRequirement { Required = 0, Optional = 1, Conditional = 2 }
public enum DisclosureState { Present = 0, NotSupplied = 1, Unresolved = 2, FromNumberReservation = 3, NotDisclosed = 4 }
// A frozen projection of currently supported data, not an XML tree or a claim of full FA(3) coverage.
public sealed record ApprovedDisclosureField(string Path, DisclosureRequirement Requirement, DisclosureState State, System.Text.Json.JsonElement? Value);
public sealed record ApprovedSalesInvoiceDisclosure(string? Version, IReadOnlyList<ApprovedDisclosureField>? Fields = null, IReadOnlyList<string>? InternalOnlyPaths = null);

public sealed record ApprovedSalesInvoiceMetadata(int? SourceStatus, string? SourceRowVersion, DateTimeOffset? CreatedAt,
    DateTimeOffset? UpdatedAt, IReadOnlyList<ApprovedExchangeRateHistory>? ExchangeRateHistory,
    IReadOnlyList<ApprovedSalesInvoiceIssue>? MissingData,
    string? InvoiceNumber, string? KsefNumber, Guid? FinalNumberReservationId);

public sealed record SalesInvoiceApprovalSnapshot(Guid TenantId, Guid InvoiceId, Guid ApprovalId,
    int SourceRevision, string ApprovedBy, DateTimeOffset ApprovedAt, int? ModelVersion,
    string? CalculatorVersion, string? ProjectionVersion, string? RulesVersion, ApprovedSalesInvoiceDraft? Input,
    ApprovedSalesInvoiceCalculations? Calculations, ApprovedSalesInvoiceDisclosure? Disclosure, ApprovedSalesInvoiceMetadata? Metadata);

public interface ISalesInvoiceApprovalSnapshotReader
{
    SalesInvoiceApprovalSnapshot Read(SalesInvoiceApproval approval);
}

public interface ISalesInvoiceApprovalSnapshotWriter
{
    string Write(SalesInvoice invoice, Guid approvalId, string approvedBy, DateTimeOffset approvedAt, xKsef.Domain.SalesInvoices.Calculation.InvoiceCalculationResult? validatedCalculations = null);
}

public sealed class SalesInvoiceApprovalSnapshotException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
