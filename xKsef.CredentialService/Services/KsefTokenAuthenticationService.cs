using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Authorization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace xKsef.CredentialService.Services;

public sealed class KsefTokenAuthenticationService : IKsefTokenAuthenticationService
{
    private readonly IKsefCredentialService _credentialService;
    private readonly IAuthCoordinator _authCoordinator;
    private readonly ICryptographyService _cryptographyService;
    private readonly KsefAuthenticationLock _authenticationLock;
    private readonly IAuthorizationClient _authorizationClient;
    public KsefTokenAuthenticationService(IKsefCredentialService credentialService, IAuthCoordinator authCoordinator, ICryptographyService cryptographyService, KsefAuthenticationLock authenticationLock, IAuthorizationClient authorizationClient)
    {
        _credentialService = credentialService;
        _authCoordinator = authCoordinator;
        _cryptographyService = cryptographyService;
        _authenticationLock = authenticationLock;
        _authorizationClient = authorizationClient;
    }

    public async Task AuthenticateAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty) throw new ArgumentException("Nieprawidłowy identyfikator poświadczenia.", nameof(credentialId));
        if (tenantId == Guid.Empty) throw new ArgumentException("Nieprawidłowy identyfikator tenanta.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Nieprawidłowy identyfikator użytkownika.", nameof(userId));
        if (string.IsNullOrWhiteSpace(nip)) throw new ArgumentException("Nie podano NIP.", nameof(nip));

        SemaphoreSlim semaphore = _authenticationLock.GetLock(tenantId, userId);
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            if (await _credentialService.HasValidSessionAsync(tenantId, userId, credentialId, nip, cancellationToken)) return;

            if (await TryRefreshAccessTokenAsync(tenantId, userId, credentialId, nip, cancellationToken)) return;

            byte[]? tokenBytes = await _credentialService.GetAsync(credentialId, tenantId, userId, "Token", cancellationToken, contextNip: nip);

            if (tokenBytes == null) throw new InvalidOperationException("Nie znaleziono poświadczenia KSeF.");

            try
            {
                if (tokenBytes.Length == 0) throw new InvalidOperationException("Zapisane poświadczenie KSeF jest puste.");

                string token = Encoding.UTF8.GetString(tokenBytes);

                var tokens = await _authCoordinator.AuthKsefTokenAsync(AuthenticationTokenContextIdentifierType.Nip, nip, token, _cryptographyService, cancellationToken: cancellationToken);

                if (string.IsNullOrWhiteSpace(tokens.AccessToken?.Token) || string.IsNullOrWhiteSpace(tokens.RefreshToken?.Token)) throw new InvalidOperationException("KSeF nie zwrócił wymaganych tokenów.");
                DateTimeOffset minimumValidity = DateTimeOffset.UtcNow.AddMinutes(2);

                if (tokens.AccessToken.ValidUntil <= minimumValidity) throw new InvalidOperationException("KSeF zwrócił AccessToken o niewystarczającym okresie ważności.");
                if (tokens.RefreshToken.ValidUntil <= minimumValidity) throw new InvalidOperationException("KSeF zwrócił RefreshToken o niewystarczającym okresie ważności.");

                await _credentialService.SaveSessionTokensAsync(tenantId, userId, credentialId, nip, tokens.AccessToken.Token, tokens.AccessToken.ValidUntil, tokens.RefreshToken.Token, tokens.RefreshToken.ValidUntil, cancellationToken);
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(tokenBytes);
            }
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<bool> TryRefreshAccessTokenAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, CancellationToken cancellationToken = default)
    {
        StoredSessionToken? storedToken = await _credentialService.GetRefreshTokenAsync(tenantId, userId, sourceCredentialId, contextNip, cancellationToken);

        if (storedToken == null) return false;

        try
        {
            if (storedToken.ValidUntil <= DateTimeOffset.UtcNow.AddMinutes(2)) return false;

            string refreshToken = Encoding.UTF8.GetString(storedToken.TokenBytes);

            RefreshTokenResponse response;

            try
            {
                response = await _authorizationClient.RefreshAccessTokenAsync(refreshToken, cancellationToken);
            }
            catch (KsefApiException ex) when (ex.StatusCode == HttpStatusCode.Unauthorized)
            {
                return false;
            }

            if (string.IsNullOrWhiteSpace(response.AccessToken?.Token)) throw new InvalidOperationException("KSeF nie zwrócił nowego AccessToken.");
            if (response.AccessToken.ValidUntil <= DateTimeOffset.UtcNow.AddMinutes(2)) throw new InvalidOperationException("KSeF zwrócił AccessToken o niewystarczającym okresie ważności.");

            await _credentialService.SaveAccessTokenAsync(tenantId, userId, sourceCredentialId, contextNip, response.AccessToken.Token, response.AccessToken.ValidUntil, cancellationToken);

            return true;
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(storedToken.TokenBytes);
        }
    }

    public async Task AuthenticateCoreAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty) throw new ArgumentException("Nieprawidłowy identyfikator poświadczenia.", nameof(credentialId));
        if (tenantId == Guid.Empty) throw new ArgumentException("Nieprawidłowy identyfikator tenanta.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Nieprawidłowy identyfikator użytkownika.", nameof(userId));
        if (string.IsNullOrWhiteSpace(nip) || nip.Length != 10 || !nip.All(char.IsAsciiDigit)) throw new ArgumentException("Nieprawidłowy NIP.", nameof(nip));

        byte[]? tokenBytes = await _credentialService.GetAsync(credentialId, tenantId, userId, "Token", cancellationToken, contextNip: nip);

        if (tokenBytes == null) throw new InvalidOperationException("Nie znaleziono poświadczenia KSeF.");

        try
        {
            if (tokenBytes.Length == 0) throw new InvalidOperationException("Zapisane poświadczenie KSeF jest puste.");

            string token = Encoding.UTF8.GetString(tokenBytes);

            var tokens = await _authCoordinator.AuthKsefTokenAsync(AuthenticationTokenContextIdentifierType.Nip, nip, token, _cryptographyService, cancellationToken: cancellationToken);

            if (string.IsNullOrWhiteSpace(tokens.AccessToken?.Token) || string.IsNullOrWhiteSpace(tokens.RefreshToken?.Token)) throw new InvalidOperationException("KSeF nie zwrócił wymaganych tokenów.");

            DateTimeOffset minimumValidity = DateTimeOffset.UtcNow.AddMinutes(2);

            if (tokens.AccessToken.ValidUntil <= minimumValidity) throw new InvalidOperationException("KSeF zwrócił AccessToken o niewystarczającym okresie ważności.");
            if (tokens.RefreshToken.ValidUntil <= minimumValidity) throw new InvalidOperationException("KSeF zwrócił RefreshToken o niewystarczającym okresie ważności.");

            await _credentialService.SaveSessionTokensAsync(tenantId, userId, credentialId, nip, tokens.AccessToken.Token, tokens.AccessToken.ValidUntil, tokens.RefreshToken.Token, tokens.RefreshToken.ValidUntil, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }

}
