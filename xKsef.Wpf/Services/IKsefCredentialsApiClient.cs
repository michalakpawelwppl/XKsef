using xKsef.Application.Credentials;
using xKsef.Wpf.Http;

namespace xKsef.Wpf.Services;

public interface IKsefCredentialsApiClient
{
    Task<KsefTokenInfo> GetCertificateInfoAsync(ApiRequestContext context);
    Task<KsefTokenInfo> GetTokenInfoAsync(ApiRequestContext context);
    Task<Guid> SaveTokenAsync(ApiRequestContext context, string token);
}