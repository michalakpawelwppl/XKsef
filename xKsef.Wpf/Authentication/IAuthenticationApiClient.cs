using xKsef.Contracts.Authentication;
using xKsef.Wpf.Http;
using xKsef.Wpf.Models;

namespace xKsef.Wpf.Authentication;

public interface IAuthenticationApiClient
{
    Task<UserCreationResult> RegisterAsync(ApiRequestContext context, string login, string email, string password);
    Task<UserCreationResult> CreateUserAsync(ApiRequestContext context, string login, string email, string password);
    Task<LoginResponse?> LoginAsync(ApiRequestContext context, string login, string password);
    Task AuthenticateKsefTokenAsync(ApiRequestContext context, Guid credentialId);
    Task<Guid> SaveKsefCertificateAsync(ApiRequestContext context, string certificatePem, string encryptedPrivateKeyPem, string password);
    Task AuthenticateKsefCertificateAsync(ApiRequestContext context, Guid credentialId);
}