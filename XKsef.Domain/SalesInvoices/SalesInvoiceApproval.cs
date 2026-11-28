namespace xKsef.Domain.SalesInvoices;

/// <summary>The current approval, removed only when safely returning the invoice to Draft.</summary>
public sealed class SalesInvoiceApproval
{
    private SalesInvoiceApproval() { }
    internal SalesInvoiceApproval(Guid tenantId, Guid invoiceId, string actor, DateTimeOffset now, int sourceRevision, int approvedRevision, string snapshotJson, Guid approvalId)
    {
        Id = approvalId; TenantId = tenantId; SalesInvoiceId = invoiceId; ApprovedBy = actor; ApprovedAt = now;
        SourceRevision = sourceRevision; ApprovedRevision = approvedRevision; SnapshotJson = snapshotJson;
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SalesInvoiceId { get; private set; }
    public string ApprovedBy { get; private set; } = "";
    public DateTimeOffset ApprovedAt { get; private set; }
    public int SourceRevision { get; private set; }
    public int ApprovedRevision { get; private set; }
    // Current JSON snapshot independent of API DTOs.
    public string SnapshotJson { get; private set; } = "";
}
