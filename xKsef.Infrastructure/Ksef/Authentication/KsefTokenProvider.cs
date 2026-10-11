using KSeF.Client.Core.Interfaces.Clients;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using xKsef.Infrastructure.Ksef.Configuration;

namespace xKsef.Infrastructure.Ksef.Authentication;

public sealed class KsefTokenProvider
{
    private readonly KsefTokenStore _tokenStore;
    //private readonly IAuthorizationClient _authorizationClient;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly KsefOptions _options;

    private static readonly TimeSpan ExpirationMargin =
        TimeSpan.FromMinutes(2);

    public KsefTokenProvider(
        KsefTokenStore tokenStore,
        //IAuthorizationClient authorizationClient,
        IServiceScopeFactory scopeFactory,
        IOptions<KsefOptions> options)
    {
        _tokenStore = tokenStore;
        //_authorizationClient = authorizationClient;
        _scopeFactory = scopeFactory;
        _options = options.Value;
    }

    public async Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default)
    {
        // Szybka ścieżka: token jest nadal ważny.
        var tokens = _tokenStore.Get();

        if (IsAccessTokenValid(tokens))
            return tokens!.AccessToken;

        await _semaphore.WaitAsync(cancellationToken);

        try
        {
            // Ponowne sprawdzenie po uzyskaniu blokady.
            tokens = _tokenStore.Get();

            if (IsAccessTokenValid(tokens))
                return tokens!.AccessToken;

            if (tokens is null || tokens.RefreshTokenValidUntil <= DateTimeOffset.UtcNow)
            {
                return await AuthenticateWithCertificateAsync(cancellationToken);
            }

            await using var scope = _scopeFactory.CreateAsyncScope();

            var authorizationClient = scope.ServiceProvider
                .GetRequiredService<IAuthorizationClient>();

            var refreshed = await authorizationClient
                .RefreshAccessTokenAsync(
                    tokens.RefreshToken,
                    cancellationToken);

            if (string.IsNullOrWhiteSpace(refreshed.AccessToken?.Token))
            {
                throw new InvalidOperationException("KSeF nie zwrócił nowego accessToken.");
            }

            var updatedTokens = tokens with
            {
                AccessToken = refreshed.AccessToken.Token,
                AccessTokenValidUntil = refreshed.AccessToken.ValidUntil
            };

            _tokenStore.Set(updatedTokens);

            return updatedTokens.AccessToken;
        }
        finally
        {
            _semaphore.Release();
        }
    }

    private static bool IsAccessTokenValid(KsefTokens? tokens)
    {
        return tokens is not null &&
               !string.IsNullOrWhiteSpace(tokens.AccessToken) &&
               tokens.AccessTokenValidUntil >
                   DateTimeOffset.UtcNow.Add(ExpirationMargin);
    }

    private async Task<string> AuthenticateWithCertificateAsync(CancellationToken cancellationToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();

        var authenticationService = scope.ServiceProvider
            .GetRequiredService<KsefAuthenticationService>();

        await authenticationService.AuthenticateAsync(
                _options.Nip,
                _options.CertificatePath,
                _options.CertificatePassword,
                cancellationToken);

        var tokens = _tokenStore.Get();

        if (tokens is null ||
            !IsAccessTokenValid(tokens))
        {
            throw new InvalidOperationException(
                "Uwierzytelnianie KSeF nie zwróciło ważnego accessToken.");
        }

        return tokens.AccessToken;
    }
}