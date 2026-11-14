using xKsef.Contracts.Tenants;

namespace xKsef.Application.Tenants;

public sealed record TenantUsersResult(string? Error = null, IReadOnlyList<TenantUserDto>? Users = null, string? CurrentUserRole = null);

public interface ITenantUsersService
{
    Task<TenantUsersResult> GetAsync(Guid tenantId, string actorId, CancellationToken cancellationToken = default);
    Task<TenantUsersResult> AddAsync(Guid tenantId, string actorId, string identifier, string role, CancellationToken cancellationToken = default);
    Task<TenantUsersResult> SetRoleAsync(Guid tenantId, string actorId, string userId, string role, CancellationToken cancellationToken = default);
    Task<TenantUsersResult> RemoveAsync(Guid tenantId, string actorId, string userId, CancellationToken cancellationToken = default);
}