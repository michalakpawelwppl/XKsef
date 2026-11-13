using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Models.Authorization;
using System.Net;
using System.Security.Cryptography;
using System.Text;

namespace xKsef.CredentialService.Services;

public sealed class KsefTokenRefreshService
{
    private readonly IKsefCredentialService _credentialService;
    private readonly IAuthorizationClient _authorizationClient;

    public KsefTokenRefreshService(IKsefCredentialService credentialService, IAuthorizationClient authorizationClient)
    {
        _credentialService = credentialService;
        _authorizationClient = authorizationClient;
    }

    public async Task<bool> TryRefreshAccessTokenAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, CancellationToken cancellationToken = default)
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
            CryptographicOperations.ZeroMemory(storedToken.TokenBytes);
        }
    }
}