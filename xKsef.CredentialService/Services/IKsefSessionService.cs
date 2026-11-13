namespace xKsef.CredentialService.Services;

public interface IKsefSessionService
{
    Task EnsureAuthenticatedAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default);
   // Task<string?> GetCredentialTypeAsync(Guid credentialId, Guid tenantId, string userId, CancellationToken cancellationToken = default);
}