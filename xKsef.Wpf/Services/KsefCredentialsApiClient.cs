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

    public async Task<KsefTokenInfo> GetCertificateInfoAsync(ApiRequestContext context)
    {
        using HttpRequestMessage request = CreateRequest(HttpMethod.Get, "api/ksef/credentials/certificate-info", context);
        using HttpResponseMessage response = await _httpClient.SendAsync(request, context.CancellationToken);

        response.EnsureSuccessStatusCode();

        KsefTokenInfo? result = await response.Content.ReadFromJsonAsync<KsefTokenInfo>(cancellationToken: context.CancellationToken);

        return result ?? throw new InvalidOperationException("API nie zwróciło informacji o certyfikacie KSeF.");
    }


    private sealed class SaveTokenResponse
    {
        public Guid CredentialId { get; set; }
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, ApiRequestContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        if (context.Scope != ApiRequestScope.TenantHeader)
            throw new InvalidOperationException("KSeF credential requests require the TenantHeader scope.");
        if (string.IsNullOrWhiteSpace(context.AccessToken))
            throw new InvalidOperationException("KSeF credential requests require an access token.");
        if (context.TenantId is not Guid tenantId || tenantId == Guid.Empty)
            throw new InvalidOperationException("KSeF credential requests require a tenant ID.");

        var request = new HttpRequestMessage(method, url);
        request.SetApiRequestContext(context);
        return request;
    }

}