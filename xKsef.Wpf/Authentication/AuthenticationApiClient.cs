using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using xKsef.Contracts.Authentication;
using xKsef.Wpf.Http;
using xKsef.Wpf.Models;

namespace xKsef.Wpf.Authentication;

public sealed class AuthenticationApiClient : IAuthenticationApiClient
{
    private sealed record SaveKsefCertificateResponse(Guid CredentialId);
    private readonly HttpClient _httpClient;

    public AuthenticationApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<LoginResponse?> LoginAsync(ApiRequestContext context, string login, string password)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/auth/login", context, ApiRequestScope.Anonymous);
        request.Content = JsonContent.Create(new LoginRequest(login, password));

        using var response = await _httpClient.SendAsync(request, context.CancellationToken);
        if (response.StatusCode == HttpStatusCode.Unauthorized)
            return null;

        response.EnsureSuccessStatusCode();
        var loginResponse = await response.Content.ReadFromJsonAsync<LoginResponse>(cancellationToken: context.CancellationToken);
        if (loginResponse is null || string.IsNullOrWhiteSpace(loginResponse.AccessToken) || loginResponse.TokenType != "Bearer")
            throw new InvalidOperationException("API zwróciło nieprawidłową odpowiedź logowania.");

        return loginResponse;
    }

    public async Task AuthenticateKsefTokenAsync(ApiRequestContext context, Guid credentialId)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/ksef/authentication/authenticate-token", context, ApiRequestScope.TenantHeader);
        request.Content = JsonContent.Create(new { CredentialId = credentialId });
        using HttpResponseMessage response = await _httpClient.SendAsync(request, context.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task<Guid> SaveKsefCertificateAsync(ApiRequestContext context, string certificatePem, string encryptedPrivateKeyPem, string password)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/ksef/authentication/certificate", context, ApiRequestScope.User);
        request.Content = JsonContent.Create(new
        {
            CertificatePem = certificatePem,
            EncryptedPrivateKeyPem = encryptedPrivateKeyPem,
            Password = password
        });

        using var response = await _httpClient.SendAsync(request, context.CancellationToken);
        response.EnsureSuccessStatusCode();

        var result = await response.Content.ReadFromJsonAsync<SaveKsefCertificateResponse>(cancellationToken: context.CancellationToken);
        if (result == null || result.CredentialId == Guid.Empty)
            throw new InvalidOperationException("API nie zwróciło poprawnego CredentialId.");

        return result.CredentialId;
    }

    public async Task AuthenticateKsefCertificateAsync(ApiRequestContext context, Guid credentialId)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/ksef/authentication/authenticate-certificate", context, ApiRequestScope.TenantHeader);
        request.Content = JsonContent.Create(new { CredentialId = credentialId });
        using HttpResponseMessage response = await _httpClient.SendAsync(request, context.CancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private sealed record CreateUserResponse(string UserId);
    private sealed record UserValidationProblem(Dictionary<string, string[]>? Errors);

    public async Task<UserCreationResult> RegisterAsync(ApiRequestContext context, string login, string email, string password)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/auth/register", context, ApiRequestScope.Anonymous);
        request.Content = JsonContent.Create(new RegisterRequest { Login = login, Email = email, Password = password });
        using var response = await _httpClient.SendAsync(request, context.CancellationToken);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var result = await response.Content.ReadFromJsonAsync<RegisterResponse>(cancellationToken: context.CancellationToken);
            if (result is null || string.IsNullOrWhiteSpace(result.UserId))
                throw new InvalidOperationException("API nie zwróciło identyfikatora użytkownika.");
            return new UserCreationResult(response.StatusCode, result.UserId, Array.Empty<string>());
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<UserValidationProblem>(cancellationToken: context.CancellationToken);
            string[] errors = problem?.Errors?.SelectMany(pair => pair.Key switch
            {
                "Login" => new[] { "InvalidUserName" },
                "Email" => new[] { "InvalidEmail" },
                "Password" => new[] { "PasswordRequired" },
                "Identity" => pair.Value,
                _ => new[] { "InvalidData" }
            }).Distinct().ToArray() ?? Array.Empty<string>();
            return new UserCreationResult(response.StatusCode, null, errors);
        }

        return new UserCreationResult(response.StatusCode, null, Array.Empty<string>());
    }

    public async Task<UserCreationResult> CreateUserAsync(ApiRequestContext context, string login, string email, string password)
    {
        using var request = CreateRequest(HttpMethod.Post, "api/users", context, ApiRequestScope.User);
        request.Content = JsonContent.Create(new { Login = login, Email = email, Password = password });
        using var response = await _httpClient.SendAsync(request, context.CancellationToken);

        if (response.StatusCode == HttpStatusCode.Created)
        {
            var result = await response.Content.ReadFromJsonAsync<CreateUserResponse>(cancellationToken: context.CancellationToken);
            if (result is null || string.IsNullOrWhiteSpace(result.UserId))
                throw new InvalidOperationException("API nie zwróciło identyfikatora użytkownika.");
            return new UserCreationResult(response.StatusCode, result.UserId, Array.Empty<string>());
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var problem = await response.Content.ReadFromJsonAsync<UserValidationProblem>(cancellationToken: context.CancellationToken);
            string[] errors = problem?.Errors?.SelectMany(pair => pair.Key switch
            {
                "Login" => new[] { "InvalidUserName" },
                "Email" => new[] { "InvalidEmail" },
                "Password" => new[] { "PasswordRequired" },
                "Identity" => pair.Value,
                _ => new[] { "InvalidData" }
            }).Distinct().ToArray() ?? Array.Empty<string>();
            return new UserCreationResult(response.StatusCode, null, errors);
        }

        return new UserCreationResult(response.StatusCode, null, Array.Empty<string>());
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, ApiRequestContext context, ApiRequestScope requiredScope)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        if (context.Scope != requiredScope)
            throw new InvalidOperationException($"Authentication request requires the {requiredScope} scope.");

        if (requiredScope == ApiRequestScope.Anonymous)
        {
            if (context.AccessToken is not null || context.TenantId is not null)
                throw new InvalidOperationException("Anonymous login context cannot contain a token or tenant ID.");
        }
        else
        {
            if (string.IsNullOrWhiteSpace(context.AccessToken))
                throw new InvalidOperationException("Authentication request requires an access token.");
            if (requiredScope != ApiRequestScope.User && (context.TenantId is not Guid tenantId || tenantId == Guid.Empty))
                throw new InvalidOperationException("Authentication request requires a tenant ID.");
        }

        var request = new HttpRequestMessage(method, url);
        request.SetApiRequestContext(context);
        return request;
    }
}
