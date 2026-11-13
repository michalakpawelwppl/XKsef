namespace xKsef.CredentialService.Services;

public interface IKsefTokenAuthenticationService
{
    Task AuthenticateAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default);
    Task AuthenticateCoreAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default);


}