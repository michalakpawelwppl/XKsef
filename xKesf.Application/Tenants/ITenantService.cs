using xKsef.Domain.Tenants;
using xKsef.Contracts.Tenants;

namespace xKsef.Application.Tenants;

public interface ITenantService
{
    Task<CreateTenantResult> CreateAsync(string name, string nip, string taxEntityName, string userLogin, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TenantDto>> GetUserTenantsAsync(string userId, CancellationToken cancellationToken = default);
    Task<TaxEntity?> GetTaxEntityAsync(Guid tenantId, CancellationToken cancellationToken = default);
    Task<Guid?> GetPreferredKsefCredentialIdAsync(Guid tenantId, string userId, CancellationToken cancellationToken = default);
    Task SetPreferredKsefCredentialIdAsync(Guid tenantId, string userId, Guid? credentialId, CancellationToken cancellationToken = default);
}