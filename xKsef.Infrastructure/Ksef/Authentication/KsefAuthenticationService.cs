using KSeF.Client.Api.Builders.Auth;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Models.Authorization;

namespace xKsef.Infrastructure.Ksef.Authentication;

public class KsefAuthenticationService
{
    private readonly IAuthorizationClient _authorizationClient;
    private readonly KsefTokenStore _tokenStore;

    public KsefAuthenticationService(IAuthorizationClient authorizationClient, KsefTokenStore tokenStore)
    {
        _authorizationClient = authorizationClient;
        _tokenStore = tokenStore;
    }

    public async Task CompleteAuthenticationAsync(AuthenticationOperation operation, string userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 60;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var status = await _authorizationClient.GetAuthStatusAsync(operation.ReferenceNumber, operation.AuthenticationToken, cancellationToken);

            switch (status.Status.Code)
            {
                case 200:
                    // Uwierzytelnianie zakończone sukcesem.
                    var tokens = await _authorizationClient.GetAccessTokenAsync(operation.AuthenticationToken, cancellationToken);

                    if (string.IsNullOrWhiteSpace(tokens.AccessToken?.Token) || string.IsNullOrWhiteSpace(tokens.RefreshToken?.Token))
                    {
                        throw new InvalidOperationException("KSeF nie zwrócił wymaganych tokenów.");
                    }

                    _tokenStore.Set(userId, tenantId, new KsefTokens(AccessToken: tokens.AccessToken.Token, AccessTokenValidUntil: tokens.AccessToken.ValidUntil, RefreshToken: tokens.RefreshToken.Token, RefreshTokenValidUntil: tokens.RefreshToken.ValidUntil));

                    return;



                case 100:
                    // KSeF nadal przetwarza uwierzytelnianie.
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay( TimeSpan.FromSeconds(2), cancellationToken);
                    }
                    break;

                default:
                    throw new InvalidOperationException($"Błąd uwierzytelniania KSeF. Kod: {status.Status.Code}, Opis: {status.Status.Description}");
            }
        }

        throw new TimeoutException("KSeF nie zakończył uwierzytelniania w ciągu 120 sekund.");
    }

    public async Task<PreparedKsefAuthentication> PrepareAuthenticationXmlAsync(string nip, CancellationToken cancellationToken = default)
    {
        var challenge = await _authorizationClient.GetAuthChallengeAsync(cancellationToken);

        var request = AuthTokenRequestBuilder.Create().WithChallenge(challenge.Challenge).WithContext(AuthenticationTokenContextIdentifierType.Nip, nip).WithIdentifierType(AuthenticationTokenSubjectIdentifierTypeEnum.CertificateSubject).Build();

        return new PreparedKsefAuthentication(request.SerializeToXmlString(), challenge.Challenge);
    }

    public async Task<AuthenticationOperation> SubmitSignedAuthenticationXmlAsync(string signedXml, CancellationToken cancellationToken = default)
    {
        var operation = await _authorizationClient.SubmitXadesAuthRequestAsync(signedXml, verifyCertificateChain: false, enforceXadesCompliance: false, cancellationToken: cancellationToken);
        
        return new AuthenticationOperation
        {
            ReferenceNumber = operation.ReferenceNumber,
            AuthenticationToken = operation.AuthenticationToken.Token
        };
    }

}
