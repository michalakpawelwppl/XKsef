namespace xKsef.Application.Credentials;

public interface ICredentialServiceClient
{
    Task<Guid> SaveTokenAsync(Guid tenantId, string userId, string token, string contextNip, CancellationToken cancellationToken = default);

    Task<KsefTokenInfo> GetTokenInfoAsync(Guid tenantId, string userId, string contextNip, CancellationToken cancellationToken = default);
    Task AuthenticateTokenAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default);
    Task<KsefAccessTokenResponse> GetAccessTokenAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default);
    Task<Guid> SaveCertificateAsync(string userId, string certificatePem, string encryptedPrivateKeyPem, string password, CancellationToken cancellationToken = default);
    Task AuthenticateCertificateAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default);
    Task<KsefTokenInfo> GetCertificateInfoAsync(string userId, CancellationToken cancellationToken = default);
    Task EnsureAuthenticatedAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KsefCertificateInfo>> GetCertificatesAsync(string userId, CancellationToken cancellationToken = default);
    Task<string?> GetCredentialTypeAsync(Guid credentialId, Guid tenantId, string userId, string contextNip, CancellationToken cancellationToken = default);
}