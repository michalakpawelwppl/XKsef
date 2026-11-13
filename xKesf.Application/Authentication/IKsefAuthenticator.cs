namespace xKsef.Application.Ksef.Authentication;

public interface IKsefAuthenticator
{
    Task AuthenticateAsync(Guid tenantId, string userId, Guid credentialId, CancellationToken cancellationToken = default);
}