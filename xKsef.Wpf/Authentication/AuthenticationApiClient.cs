using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using xKsef.Api.Contracts.Authentication;

namespace xKsef.Wpf.Authentication;

public sealed class AuthenticationApiClient : IAuthenticationApiClient
{
    private readonly HttpClient _httpClient;

    public AuthenticationApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LoginResponse?> LoginAsync(
        string login,
        string password,
        CancellationToken cancellationToken = default)
    {
        var request = new LoginRequest(login, password);

        using var response = await _httpClient.PostAsJsonAsync(
            "api/auth/login",
            request,
            cancellationToken);

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }

        if (response.StatusCode == HttpStatusCode.Unauthorized)
        {
            return null;
        }
        response.EnsureSuccessStatusCode();

        var loginResponse = await response.Content
            .ReadFromJsonAsync<LoginResponse>(
                cancellationToken: cancellationToken);

        if (loginResponse is null ||
            string.IsNullOrWhiteSpace(loginResponse.AccessToken) ||
            loginResponse.TokenType != "Bearer")
        {
            throw new InvalidOperationException(
                "API zwróciło nieprawidłową odpowiedź logowania.");
        }

        return loginResponse;
    }
}