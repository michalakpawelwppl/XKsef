namespace xKsef.Application.Authentication;

public interface IKsefAuthenticationService
{
    Task<bool> AuthenticateAsync(CancellationToken cancellationToken = default);
}