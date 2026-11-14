using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using xKsef.Application.Tenants;
using xKsef.Contracts.Tenants;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Persistence;
using xKsef.Infrastructure.Identity;

namespace xKsef.Infrastructure.Tenants;

public sealed class TenantService : ITenantService
{
    private readonly AppDbContext _dbContext;
    private readonly UserManager<ApplicationUser> _userManager;

    public TenantService(AppDbContext dbContext, UserManager<ApplicationUser> userManager)
    {
        _dbContext = dbContext;
        _userManager = userManager;
    }

    public async Task<CreateTenantResult> CreateAsync(string name, string nip, string taxEntityName, string userId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ApplicationUser? user = await _userManager.FindByIdAsync(userId);
        if (user is null)
            return new CreateTenantResult(null, "UserNotFound");

        TaxEntity? taxEntity = await _dbContext.TaxEntities.SingleOrDefaultAsync(x => x.Nip == nip, cancellationToken);
        bool isNewTaxEntity = taxEntity is null;
        if (taxEntity is null)
        {
            taxEntity = new TaxEntity { Id = Guid.NewGuid(), Nip = nip, Name = taxEntityName.Trim() };
            _dbContext.TaxEntities.Add(taxEntity);
        }

        var tenant = new Tenant { Id = Guid.NewGuid(), Name = name.Trim(), IsActive = true, TaxEntityId = taxEntity.Id };
        var userTenant = new UserTenant { TenantId = tenant.Id, UserId = user.Id, Role = "Admin" };
        _dbContext.Tenants.Add(tenant);
        _dbContext.UserTenants.Add(userTenant);

        try
        {
            // SQL Server wraps these dependent inserts in one transaction with the default EF Core settings.
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (isNewTaxEntity
            && ex.InnerException is SqlException { Number: 2601 or 2627 }
            && ex.Entries.Any(entry => entry.Entity is TaxEntity))
        {
            _dbContext.Entry(userTenant).State = EntityState.Detached;
            _dbContext.Entry(tenant).State = EntityState.Detached;
            _dbContext.Entry(taxEntity).State = EntityState.Detached;
            return new CreateTenantResult(null, "NipConflict");
        }

        return new CreateTenantResult(tenant.Id, null);
    }

    public async Task<IReadOnlyList<TenantDto>> GetUserTenantsAsync(string userId, CancellationToken cancellationToken = default)
    {
        return await _dbContext.UserTenants
            .AsNoTracking()
            .Where(ut => ut.UserId == userId)
            .Join(_dbContext.Tenants.Where(t => t.IsActive), ut => ut.TenantId, t => t.Id, (ut, t) => new TenantDto(t.Id, t.Name, ut.Role))
            .ToListAsync(cancellationToken);
    }
    public async Task<TaxEntity?> GetTaxEntityAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty)
            throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));

        return await _dbContext.Tenants.AsNoTracking().Where(t => t.Id == tenantId && t.IsActive).Select(t => t.TaxEntity).FirstOrDefaultAsync(cancellationToken);
    }
    public async Task<Guid?> GetPreferredKsefCredentialIdAsync(Guid tenantId, string userId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId nie może być pusty.", nameof(userId));

        UserTenant? userTenant = await _dbContext.UserTenants.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && _dbContext.Tenants.Any(t => t.Id == tenantId && t.IsActive), cancellationToken);

        if (userTenant == null) throw new InvalidOperationException("Użytkownik nie jest przypisany do wskazanego tenanta.");

        return userTenant.PreferredKsefCredentialId;
    }
    public async Task SetPreferredKsefCredentialIdAsync(Guid tenantId, string userId, Guid? credentialId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId nie może być pusty.", nameof(userId));
        if (credentialId == Guid.Empty) throw new ArgumentException("CredentialId nie może być pusty.", nameof(credentialId));

        UserTenant? userTenant = await _dbContext.UserTenants.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && _dbContext.Tenants.Any(t => t.Id == tenantId && t.IsActive), cancellationToken);

        if (userTenant == null) throw new InvalidOperationException("Użytkownik nie jest przypisany do wskazanego tenanta.");

        userTenant.PreferredKsefCredentialId = credentialId;

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

}