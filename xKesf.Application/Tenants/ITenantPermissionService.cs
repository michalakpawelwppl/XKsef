namespace xKsef.Application.Tenants;

public static class TenantPermissions
{
    public const string SalesInvoicesIssue = "SalesInvoices.Issue";
    public const string InvoiceNumberSeriesManage = "InvoiceNumberSeries.Manage";
    // Reserved for a future command; declaring the permission does not expose that operation.
    public const string SalesInvoicesUnapprove = "SalesInvoices.Unapprove";
}

public interface ITenantPermissionService
{
    Task<bool> HasPermissionAsync(string userId, Guid tenantId, string permission, CancellationToken cancellationToken = default);
}

public sealed class TenantPermissionDeniedException(string permission) : Exception("Brak wymaganego uprawnienia: " + permission)
{
    public string Permission { get; } = permission;
}
