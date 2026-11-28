using xKsef.Application.Tenants;
using xKsef.Contracts.Tenants;

namespace xKsef.Infrastructure.Tenants;

public sealed class TenantPermissionService(ITenantAccessService access) : ITenantPermissionService
{
    public async Task<bool> HasPermissionAsync(string userId, Guid tenantId, string permission, CancellationToken cancellationToken = default)
    {
        if (permission is not (TenantPermissions.SalesInvoicesIssue or TenantPermissions.SalesInvoicesUnapprove or TenantPermissions.InvoiceNumberSeriesManage)) return false;
        var roles = await access.GetRolesAsync(userId, tenantId, cancellationToken);
        // Current tenant membership and role are read from storage, never from JWT role claims.
        return roles.Contains(TenantRoles.Admin, StringComparer.Ordinal);
    }
}
