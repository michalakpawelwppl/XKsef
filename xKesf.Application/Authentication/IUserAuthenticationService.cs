namespace xKsef.Application.Authentication;

public interface IUserAuthenticationService
{
    Task<AuthenticatedUser?> AuthenticateAsync(
        string login,
        string password,
        CancellationToken cancellationToken = default);
}