using System.Net.Http;
using System.Net.Http.Json;
using xKsef.Application.Credentials;
using xKsef.Wpf.Http;

namespace xKsef.Wpf.Services;

public sealed class KsefCredentialsApiClient : IKsefCredentialsApiClient
{
    private readonly HttpClient _httpClient;

    public KsefCredentialsApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<KsefTokenInfo> GetTokenInfoAsync(ApiRequestContext context)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "api/ksef/credentials/token", context);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, context.CancellationToken);
        response.EnsureSuccessStatusCode();

        KsefTokenInfo? result = await response.Content.ReadFromJsonAsync<KsefTokenInfo>(cancellationToken: context.CancellationToken);

        return result ?? throw new InvalidOperationException("API nie zwróciło informacji o tokenie KSeF.");
    }

    public async Task<Guid> SaveTokenAsync(ApiRequestContext context, string token)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Post, "api/ksef/credentials/token", context);
        request.Content = JsonContent.Create(new { Token = token });
        using HttpResponseMessage response = await _httpClient.SendAsync(request, context.CancellationToken);
        response.EnsureSuccessStatusCode();

        SaveTokenResponse? result = await response.Content.ReadFromJsonAsync<SaveTokenResponse>(cancellationToken: context.CancellationToken);

        return result?.CredentialId ?? throw new InvalidOperationException("API nie zwróciło identyfikatora poświadczenia.");
    }

    public async Task<IReadOnlyList<KsefCertificateInfo>> GetCertificatesAsync(ApiRequestContext context)
    {
        using var request = CreateRequest(HttpMethod.Get, "api/ksef/credentials/certificates", context, ApiRequestScope.User);
        using var response = await _httpClient.SendAsync(request, context.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<KsefCertificateInfo[]>(cancellationToken: context.CancellationToken)
            ?? throw new InvalidOperationException("API nie zwróciło listy certyfikatów.");
    }
    public async Task<KsefTokenInfo> GetCertificateInfoAsync(ApiRequestContext context)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "api/ksef/credentials/certificate-info", context, ApiRequestScope.User);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, context.CancellationToken);

        response.EnsureSuccessStatusCode();

        KsefTokenInfo? result = await response.Content.ReadFromJsonAsync<KsefTokenInfo>(cancellationToken: context.CancellationToken);

        return result ?? throw new InvalidOperationException("API nie zwróciło informacji o certyfikacie KSeF.");
    }


    private sealed class SaveTokenResponse
    {
        public Guid CredentialId { get; set; }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, ApiRequestContext context, ApiRequestScope scope = ApiRequestScope.TenantHeader)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        if (context.Scope != scope)
            throw new InvalidOperationException($"KSeF credential request requires the {scope} scope.");
        if (string.IsNullOrWhiteSpace(context.AccessToken))
            throw new InvalidOperationException("KSeF credential requests require an access token.");
        if (scope == ApiRequestScope.TenantHeader && (context.TenantId is not Guid tenantId || tenantId == Guid.Empty))
            throw new InvalidOperationException("KSeF credential requests require a tenant ID.");

        var request = new HttpRequestMessage(method, url);
        request.SetApiRequestContext(context);
        return request;
    }

}