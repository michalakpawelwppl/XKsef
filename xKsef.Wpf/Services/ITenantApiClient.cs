using xKsef.Contracts.Tenants;
using xKsef.Wpf.Http;
using xKsef.Wpf.Models;

public interface ITenantApiClient
{
    Task<TenantCreationResult> CreateTenantAsync(ApiRequestContext context, string name, string nip, string taxEntityName);
    Task<IReadOnlyList<TenantDto>> GetMyTenantsAsync(ApiRequestContext context);
    Task<IReadOnlyList<TenantDto>> GetMyTenantsAsync(CancellationToken cancellationToken = default);
    Task<Guid?> GetPreferredKsefCredentialIdAsync(ApiRequestContext context);
    Task SetPreferredKsefCredentialIdAsync(ApiRequestContext context, Guid? credentialId);
}