using System.Net.Http.Json;
using xKsef.Application.Credentials;

namespace xKsef.Infrastructure.Credentials;

public sealed class CredentialServiceClient : ICredentialServiceClient
{
    private sealed record SaveCertificateResponse(Guid CredentialId);
    private readonly IHttpClientFactory _httpClientFactory;

    private sealed record CredentialTypeResponse(string CredentialType);

    public CredentialServiceClient(IHttpClientFactory httpClientFactory)
    {
        _httpClientFactory = httpClientFactory;
    }

    public async Task<Guid> SaveTokenAsync(Guid tenantId, string userId, string token, string contextNip, CancellationToken cancellationToken = default)
    {
        HttpClient client = _httpClientFactory.CreateClient("CredentialService");

        var request = new
        {
            TenantId = tenantId,
            UserId = userId,
            Token = token,
            ContextNip = contextNip
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync("/internal/ksef-credentials/token", request, cancellationToken);
        response.EnsureSuccessStatusCode();

        SaveTokenResponse? result = await response.Content.ReadFromJsonAsync<SaveTokenResponse>(cancellationToken: cancellationToken);

        return result?.CredentialId ?? throw new InvalidOperationException("CredentialService nie zwrócił identyfikatora poświadczenia.");
    }

    private sealed class SaveTokenResponse
    {
        public Guid CredentialId { get; set; }
    }

    public async Task<KsefTokenInfo> GetTokenInfoAsync(Guid tenantId, string userId, string contextNip, CancellationToken cancellationToken = default)
    {
        HttpClient client = _httpClientFactory.CreateClient("CredentialService");
        string url = $"/internal/ksef-credentials/token?tenantId={tenantId:D}&userId={Uri.EscapeDataString(userId)}&contextNip={Uri.EscapeDataString(contextNip)}";

        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        KsefTokenInfo? result = await response.Content.ReadFromJsonAsync<KsefTokenInfo>(cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("CredentialService nie zwrócił informacji o tokenie KSeF.");
    }

    public async Task AuthenticateTokenAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default)
    {
        HttpClient client = _httpClientFactory.CreateClient("CredentialService");

        var request = new
        {
            CredentialId = credentialId,
            TenantId = tenantId,
            UserId = userId,
            Nip = nip
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync("/internal/ksef-credentials/authenticate-token", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task AuthenticateCertificateAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default)
    {
        HttpClient client = _httpClientFactory.CreateClient("CredentialService");

        var request = new
        {
            CredentialId = credentialId,
            TenantId = tenantId,
            UserId = userId,
            Nip = nip
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync("/internal/ksef-credentials/authenticate-certificate", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<KsefAccessTokenResponse> GetAccessTokenAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default)
    {
        HttpClient client = _httpClientFactory.CreateClient("CredentialService");

        string url = $"/internal/ksef-credentials/access-token?credentialId={credentialId}&tenantId={tenantId}&userId={Uri.EscapeDataString(userId)}&nip={Uri.EscapeDataString(nip)}";

        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);
        response.EnsureSuccessStatusCode();

        KsefAccessTokenResponse? result = await response.Content.ReadFromJsonAsync<KsefAccessTokenResponse>(cancellationToken: cancellationToken);

        if (result == null || string.IsNullOrWhiteSpace(result.AccessToken)) throw new InvalidOperationException("CredentialService nie zwrócił prawidłowego AccessToken.");

        return result;
    }

    public async Task<Guid> SaveCertificateAsync(string userId, string certificatePem, string encryptedPrivateKeyPem, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Brak UserId.", nameof(userId));

        var client = _httpClientFactory.CreateClient("CredentialService");

        var request = new
        {
            UserId = userId,
            CertificatePem = certificatePem,
            EncryptedPrivateKeyPem = encryptedPrivateKeyPem,
            Password = password
        };

        using var response = await client.PostAsJsonAsync("internal/ksef-credentials/certificate", request, cancellationToken);

        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SaveCertificateResponse>(cancellationToken: cancellationToken);

        if (result == null || result.CredentialId == Guid.Empty) throw new InvalidOperationException("CredentialService nie zwrócił poprawnego CredentialId.");

        return result.CredentialId;
    }

    public async Task<KsefTokenInfo> GetCertificateInfoAsync(string userId, CancellationToken cancellationToken = default)
    {
        HttpClient client = _httpClientFactory.CreateClient("CredentialService");

        KsefTokenInfo? result = await client.GetFromJsonAsync<KsefTokenInfo>($"/internal/ksef-credentials/certificate?userId={Uri.EscapeDataString(userId)}", cancellationToken);

        return result ?? throw new InvalidOperationException("CredentialService nie zwrócił informacji o certyfikacie KSeF.");
    }
    public async Task EnsureAuthenticatedAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default)
    {
        HttpClient client = _httpClientFactory.CreateClient("CredentialService");

        var request = new
        {
            CredentialId = credentialId,
            TenantId = tenantId,
            UserId = userId,
            Nip = nip
        };

        using HttpResponseMessage response = await client.PostAsJsonAsync("/internal/ksef-credentials/ensure-authenticated", request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<IReadOnlyList<KsefCertificateInfo>> GetCertificatesAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        var client = _httpClientFactory.CreateClient("CredentialService");
        return await client.GetFromJsonAsync<KsefCertificateInfo[]>($"/internal/ksef-credentials/certificates?userId={Uri.EscapeDataString(userId)}", cancellationToken)
            ?? throw new InvalidOperationException("CredentialService returned no certificate list.");
    }

    public async Task<string?> GetCredentialTypeAsync(Guid credentialId, Guid tenantId, string userId, string contextNip, CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty) throw new ArgumentException("Nieprawidłowy CredentialId.", nameof(credentialId));
        if (tenantId == Guid.Empty) throw new ArgumentException("Nieprawidłowy TenantId.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Nieprawidłowy UserId.", nameof(userId));

        HttpClient client = _httpClientFactory.CreateClient("CredentialService");

        string url = $"/internal/ksef-credentials/credential-type?credentialId={credentialId:D}&tenantId={tenantId:D}&userId={Uri.EscapeDataString(userId)}&contextNip={Uri.EscapeDataString(contextNip)}";

        using HttpResponseMessage response = await client.GetAsync(url, cancellationToken);

        if (response.StatusCode == System.Net.HttpStatusCode.NotFound) return null;

        response.EnsureSuccessStatusCode();

        CredentialTypeResponse? result = await response.Content.ReadFromJsonAsync<CredentialTypeResponse>(cancellationToken: cancellationToken);

        return result?.CredentialType ?? throw new InvalidOperationException("CredentialService nie zwrócił typu poświadczenia.");
    }

}