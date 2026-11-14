using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using xKsef.Application.Tenants;
using xKsef.Contracts.Tenants;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Infrastructure.Tenants;

public sealed class TenantUsersService(AppDbContext db, UserManager<ApplicationUser> users) : ITenantUsersService
{
    public Task<TenantUsersResult> GetAsync(Guid tenantId, string actorId, CancellationToken cancellationToken = default)
        => ExecuteAsync(tenantId, actorId, async () => new TenantUsersResult(Users: await db.UserTenants.AsNoTracking()
            .Where(member => member.TenantId == tenantId).Join(db.Users, member => member.UserId, user => user.Id,
                (member, user) => new { user.Id, user.UserName, user.Email, member.Role }).OrderBy(user => user.UserName)
            .Select(user => new TenantUserDto(user.Id, user.UserName!, user.Email, user.Role)).ToArrayAsync(cancellationToken)), cancellationToken);

    public Task<TenantUsersResult> AddAsync(Guid tenantId, string actorId, string identifier, string role, CancellationToken cancellationToken = default)
        => ExecuteAsync(tenantId, actorId, async () =>
        {
            if (string.IsNullOrWhiteSpace(identifier) || identifier.Length > 256 || !TenantRoles.IsValid(role)) return new("InvalidData");
            var byLogin = await users.FindByNameAsync(identifier.Trim());
            var byEmail = await users.FindByEmailAsync(identifier.Trim());
            if (byLogin is not null && byEmail is not null && byLogin.Id != byEmail.Id) return new("AmbiguousUser");
            var user = byLogin ?? byEmail;
            if (user is null) return new("UserNotFound");
            if (await db.UserTenants.AnyAsync(member => member.TenantId == tenantId && member.UserId == user.Id, cancellationToken)) return new("AlreadyMember");
            db.UserTenants.Add(new UserTenant { TenantId = tenantId, UserId = user.Id, Role = role });
            await db.SaveChangesAsync(cancellationToken);
            return new();
        }, cancellationToken);

    public Task<TenantUsersResult> SetRoleAsync(Guid tenantId, string actorId, string userId, string role, CancellationToken cancellationToken = default)
        => ChangeAsync(tenantId, actorId, userId, role, remove: false, cancellationToken);

    public Task<TenantUsersResult> RemoveAsync(Guid tenantId, string actorId, string userId, CancellationToken cancellationToken = default)
        => ChangeAsync(tenantId, actorId, userId, null, remove: true, cancellationToken);

    private Task<TenantUsersResult> ChangeAsync(Guid tenantId, string actorId, string userId, string? role, bool remove, CancellationToken cancellationToken)
        => ExecuteAsync(tenantId, actorId, async () =>
        {
            if (string.IsNullOrWhiteSpace(userId) || userId.Length > 450 || !remove && !TenantRoles.IsValid(role)) return new("InvalidData");
            var member = await db.UserTenants.SingleOrDefaultAsync(member => member.TenantId == tenantId && member.UserId == userId, cancellationToken);
            if (member is null) return new("MemberNotFound");
            if (string.Equals(member.Role, TenantRoles.Admin, StringComparison.OrdinalIgnoreCase) && (remove || role != TenantRoles.Admin)
                && await db.UserTenants.CountAsync(member => member.TenantId == tenantId && member.Role == TenantRoles.Admin, cancellationToken) <= 1)
                return new("LastAdmin");
            if (remove) db.UserTenants.Remove(member);
            else member.Role = role!;
            await db.SaveChangesAsync(cancellationToken);
            return new();
        }, cancellationToken);

    private async Task<TenantUsersResult> ExecuteAsync(Guid tenantId, string actorId, Func<Task<TenantUsersResult>> operation, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(actorId)) return new("InvalidData");
        try
        {
            await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.ReadCommitted, cancellationToken);
            // All membership changes take the same per-tenant lock. Authorization is checked AFTER waiting
            // for the lock, so a removed or demoted admin cannot finish an already queued operation.
            var tenant = await db.Tenants.FromSqlInterpolated($"SELECT * FROM [Tenants] WITH (UPDLOCK,HOLDLOCK) WHERE [Id] = {tenantId}")
                .AsNoTracking().SingleOrDefaultAsync(cancellationToken);
            if (tenant is null || !tenant.IsActive || !await db.UserTenants.AnyAsync(member => member.TenantId == tenantId && member.UserId == actorId && member.Role == TenantRoles.Admin, cancellationToken))
                return new("Forbidden");
            var result = await operation();
            if (result.Error is not null) return result;
            string? ownRole = await db.UserTenants.AsNoTracking().Where(member => member.TenantId == tenantId && member.UserId == actorId)
                .Select(member => member.Role).SingleOrDefaultAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result with { CurrentUserRole = ownRole };
        }
        catch (DbUpdateException error) when (error.InnerException is SqlException { Number: 2601 or 2627 or 547 or 1205 }) { return new("MembershipConflict"); }
        catch (SqlException error) when (error.Number == 1205) { return new("MembershipConflict"); }
    }
}