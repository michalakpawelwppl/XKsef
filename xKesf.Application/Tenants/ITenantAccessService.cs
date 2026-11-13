namespace xKsef.Application.Tenants;

public interface ITenantAccessService
{
    Task<bool> HasAccessAsync(string userId, Guid tenantId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<string>> GetRolesAsync(string userId, Guid tenantId, CancellationToken cancellationToken = default);
}