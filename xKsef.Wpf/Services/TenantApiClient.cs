using System.Net.Http;
using System.Net;
using xKsef.Wpf.Http;
using xKsef.Wpf.Models;
using System.Net.Http.Json;
using xKsef.Contracts.Tenants;

namespace xKsef.Wpf.Services;

public sealed class TenantApiClient : ITenantApiClient
{
    private sealed record PreferredKsefCredentialResponse(Guid? CredentialId);
    private readonly HttpClient _httpClient;

    public TenantApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    private sealed record CreateTenantResponse(Guid TenantId);
    private sealed record TenantValidationProblem(Dictionary<string, string[]>? Errors);

    public async Task<TenantCreationResult> CreateTenantAsync(ApiRequestContext context, string name, string nip, string taxEntityName, string userLogin)
    {
        using var request = CreateUserScopedRequest(HttpMethod.Post, context);
        request.Content = JsonContent.Create(new { Name = name, Nip = nip, TaxEntityName = taxEntityName, UserLogin = userLogin });
        using var response = await _httpClient.SendAsync(request, context.CancellationToken);
        if (response.StatusCode == HttpStatusCode.Created)
        {
            var result = await response.Content.ReadFromJsonAsync<CreateTenantResponse>(cancellationToken: context.CancellationToken);
            if (result is null || result.TenantId == Guid.Empty)
                throw new InvalidOperationException("API nie zwróciło identyfikatora tenanta.");
            return new TenantCreationResult(response.StatusCode, result.TenantId, Array.Empty<string>());
        }
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<TenantValidationProblem>(cancellationToken: context.CancellationToken);
            string[] errors = problem?.Errors?.Select(pair => pair.Key switch
            {
                "Name" => "InvalidName",
                "Nip" => "InvalidNip",
                "TaxEntityName" => "InvalidTaxEntityName",
                "UserLogin" => pair.Value.Contains("UserNotFound") ? "UserNotFound" : "InvalidUserLogin",
                _ => "InvalidData"
            }).Distinct().ToArray() ?? Array.Empty<string>();
            return new TenantCreationResult(response.StatusCode, null, errors);
        }
        return new TenantCreationResult(response.StatusCode, null, Array.Empty<string>());
    }

    public async Task<IReadOnlyList<TenantDto>> GetMyTenantsAsync(ApiRequestContext context)
    {
        using var request = CreateUserScopedRequest(HttpMethod.Get, context);
        using var response = await _httpClient.SendAsync(request, context.CancellationToken);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<List<TenantDto>>(cancellationToken: context.CancellationToken) ?? new List<TenantDto>();
    }

    private static HttpRequestMessage CreateUserScopedRequest(HttpMethod method, ApiRequestContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();
        if (context.Scope != ApiRequestScope.User || string.IsNullOrWhiteSpace(context.AccessToken))
            throw new InvalidOperationException("Tenant management requires a user context with an access token.");
        var request = new HttpRequestMessage(method, "api/tenants");
        request.SetApiRequestContext(context);
        return request;
    }

    public async Task<IReadOnlyList<TenantDto>> GetMyTenantsAsync(CancellationToken cancellationToken = default)
    {
        using var response = await _httpClient.GetAsync("api/tenants", cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<TenantDto>>(cancellationToken: cancellationToken) ?? new List<TenantDto>();
    }

    public async Task<Guid?> GetPreferredKsefCredentialIdAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Nieprawidłowy TenantId.", nameof(tenantId));

        using HttpResponseMessage response = await _httpClient.GetAsync($"api/tenants/{tenantId:D}/preferred-ksef-credential", cancellationToken);

        response.EnsureSuccessStatusCode();

        PreferredKsefCredentialResponse? result = await response.Content.ReadFromJsonAsync<PreferredKsefCredentialResponse>(cancellationToken: cancellationToken);

        return result?.CredentialId;
    }
    public async Task SetPreferredKsefCredentialIdAsync(Guid tenantId, Guid? credentialId, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Nieprawidłowy TenantId.", nameof(tenantId));
        if (credentialId == Guid.Empty) throw new ArgumentException("Nieprawidłowy CredentialId.", nameof(credentialId));

        var request = new SetPreferredKsefCredentialRequest(credentialId);

        using HttpResponseMessage response = await _httpClient.PutAsJsonAsync($"api/tenants/{tenantId:D}/preferred-ksef-credential", request, cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}