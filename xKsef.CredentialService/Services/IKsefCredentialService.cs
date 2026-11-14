using xKsef.CredentialService.Contracts;

namespace xKsef.CredentialService.Services;

public interface IKsefCredentialService
{
    Task<Guid> SaveAsync(Guid? tenantId, string userId, string credentialType, byte[] data, CancellationToken cancellationToken = default, string? contextNip = null);
    Task<byte[]?> GetAsync(Guid credentialId, Guid? tenantId, string userId, string credentialType, CancellationToken cancellationToken = default, string? contextNip = null);
    Task<Guid?> FindCredentialIdAsync(Guid? tenantId, string userId, string credentialType, CancellationToken cancellationToken = default, string? contextNip = null);
    Task SaveSessionTokensAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, string accessToken, DateTimeOffset accessTokenValidUntil, string refreshToken, DateTimeOffset refreshTokenValidUntil, CancellationToken cancellationToken = default);
    Task<bool> HasValidSessionAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, CancellationToken cancellationToken = default);
    Task<StoredSessionToken?> GetRefreshTokenAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, CancellationToken cancellationToken = default);
    Task SaveAccessTokenAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, string accessToken, DateTimeOffset validUntil, CancellationToken cancellationToken = default);
    Task<StoredSessionToken?> GetAccessTokenAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KsefCertificateInfo>> GetCertificatesAsync(string userId, CancellationToken cancellationToken = default);
    Task<string?> GetCredentialTypeAsync(Guid credentialId, Guid tenantId, string userId, string contextNip, CancellationToken cancellationToken = default);
}