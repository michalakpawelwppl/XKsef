using xKsef.Api.Contracts.Authentication;

namespace xKsef.Wpf.Authentication;

public interface IAuthenticationApiClient
{
    Task<LoginResponse?> LoginAsync(
        string login,
        string password,
        CancellationToken cancellationToken = default);
}