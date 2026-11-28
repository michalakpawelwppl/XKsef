using xKsef.Application.Tenants;
using xKsef.Infrastructure.Tenants;

namespace xKsef.Tests.Infrastructure;

public sealed class TenantPermissionServiceTests
{
    [Theory]
    [InlineData("Admin", "SalesInvoices.Issue", true)]
    [InlineData("User", "SalesInvoices.Issue", false)]
    [InlineData("Admin", "SalesInvoices.Unapprove", true)]
    [InlineData("User", "SalesInvoices.Unapprove", false)]
    [InlineData("Admin", "InvoiceNumberSeries.Manage", true)]
    [InlineData("User", "InvoiceNumberSeries.Manage", false)]
    [InlineData("Admin", "Unknown", false)]
    public async Task PermissionRequiresExplicitRoleGrantInTheSelectedTenant(string role, string permission, bool expected)
    {
        var tenant = Guid.NewGuid(); var access = new Access(tenant, role); var service = new TenantPermissionService(access);
        Assert.Equal(expected, await service.HasPermissionAsync("user", tenant, permission));
        Assert.False(await service.HasPermissionAsync("user", Guid.NewGuid(), permission));
        Assert.False(await service.HasPermissionAsync("other", tenant, permission));
    }
    private sealed class Access(Guid tenant, string role) : ITenantAccessService
    {
        public Task<bool> HasAccessAsync(string userId, Guid tenantId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Membership alone is not a permission.");
        public Task<IReadOnlyList<string>> GetRolesAsync(string userId, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>(userId == "user" && tenantId == tenant ? [role] : []);
    }
}
