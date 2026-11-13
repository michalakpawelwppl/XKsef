namespace xKsef.CredentialService.Services;

public interface IKsefCertificateAuthenticationService
{
    Task AuthenticateAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default);
}