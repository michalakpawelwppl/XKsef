using Microsoft.EntityFrameworkCore;
using xKsef.Application.Tenants;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Infrastructure.Tenants;

public sealed class TenantAccessService : ITenantAccessService
{
    private readonly AppDbContext _dbContext;

    public TenantAccessService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasAccessAsync(string userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || tenantId == Guid.Empty)
            return false;

        return await _dbContext.UserTenants.AsNoTracking().AnyAsync(ut => ut.UserId == userId && ut.TenantId == tenantId && _dbContext.Tenants.Any(t => t.Id == ut.TenantId && t.IsActive), cancellationToken);
    }

    public async Task<IReadOnlyList<string>> GetRolesAsync(string userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || tenantId == Guid.Empty) return [];

        return await _dbContext.UserTenants.AsNoTracking().Where(ut => ut.UserId == userId && ut.TenantId == tenantId && _dbContext.Tenants.Any(t => t.Id == ut.TenantId && t.IsActive)).Select(ut => ut.Role).Distinct().ToListAsync(cancellationToken);
    }
}