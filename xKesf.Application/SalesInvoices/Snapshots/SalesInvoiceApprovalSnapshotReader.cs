using xKsef.Domain.SalesInvoices;

namespace xKsef.Application.SalesInvoices.Snapshots;

public sealed class SalesInvoiceApprovalSnapshotReader : ISalesInvoiceApprovalSnapshotReader
{
    public SalesInvoiceApprovalSnapshot Read(SalesInvoiceApproval approval)
    {
        ArgumentNullException.ThrowIfNull(approval);
        try
        {
            var p = SalesInvoiceSnapshotJson.Read<SalesInvoiceApprovalSnapshot>(approval.SnapshotJson);
            if (p.TenantId != approval.TenantId || p.ApprovalId != approval.Id || p.InvoiceId != approval.SalesInvoiceId || p.SourceRevision != approval.SourceRevision
                || (long)p.SourceRevision + 1 != approval.ApprovedRevision || p.ApprovedBy != approval.ApprovedBy || p.ApprovedAt != approval.ApprovedAt)
                throw new SalesInvoiceApprovalSnapshotException("SnapshotIdentityMismatch", "Snapshot nie odpowiada zatwierdzonej fakturze, tenantowi lub rewizji.");
            SalesInvoiceSnapshotValidation.Validate(p);
            return p;
        }
        catch (Exception error) when (error is OverflowException or InvalidOperationException or ArgumentException or KeyNotFoundException)
        {
            throw new SalesInvoiceApprovalSnapshotException("SnapshotInvalid", "Snapshot zawiera uszkodzone dane lub nieprawidłowe powiązania.");
        }
    }
}
