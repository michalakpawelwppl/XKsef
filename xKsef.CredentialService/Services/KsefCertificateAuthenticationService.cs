using KSeF.Client.Api.Builders.Auth;
using KSeF.Client.Api.Services;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Models.Authorization;

namespace xKsef.CredentialService.Services;

public sealed class KsefCertificateAuthenticationService : IKsefCertificateAuthenticationService
{
    private readonly KsefCertificateService _certificateService;
    private readonly IAuthorizationClient _authorizationClient;
    private readonly IKsefCredentialService _credentialService;

    public KsefCertificateAuthenticationService(KsefCertificateService certificateService, IAuthorizationClient authorizationClient, IKsefCredentialService credentialService)
    {
        _certificateService = certificateService;
        _authorizationClient = authorizationClient;
        _credentialService = credentialService;
    }
    private async Task<string> SignAuthenticationXmlAsync(Guid credentialId, Guid tenantId, string userId, string xml, CancellationToken cancellationToken = default)
    {
        using var certificate = await _certificateService.GetCertificateAsync(credentialId, tenantId, userId, cancellationToken);

        if (certificate == null) throw new InvalidOperationException("Nie znaleziono certyfikatu KSeF.");
        if (!certificate.HasPrivateKey) throw new InvalidOperationException("Certyfikat KSeF nie zawiera klucza prywatnego.");

        return SignatureService.Sign(xml, certificate);
    }

    private async Task<string> GetAuthenticationChallengeAsync(CancellationToken cancellationToken = default)
    {
        var challenge = await _authorizationClient.GetAuthChallengeAsync(cancellationToken);
        return challenge.Challenge;
    }

    private async Task<string> PrepareAuthenticationXmlAsync(string nip, CancellationToken cancellationToken = default)
    {
        var challenge = await _authorizationClient.GetAuthChallengeAsync(cancellationToken);

        var request = AuthTokenRequestBuilder.Create().WithChallenge(challenge.Challenge).WithContext(AuthenticationTokenContextIdentifierType.Nip, nip).WithIdentifierType(AuthenticationTokenSubjectIdentifierTypeEnum.CertificateSubject).Build();

        return request.SerializeToXmlString();
    }

    private async Task<string> PrepareSignedAuthenticationXmlAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default)
    {
        string xml = await PrepareAuthenticationXmlAsync(nip, cancellationToken);

        return await SignAuthenticationXmlAsync(credentialId, tenantId, userId, xml, cancellationToken);
    }
    private async Task<SignatureResponse> SendSignedAuthenticationXmlAsync(string signedXml, CancellationToken cancellationToken = default)
    {
        return await _authorizationClient.SubmitXadesAuthRequestAsync(signedXml, cancellationToken: cancellationToken);
    }

    private async Task WaitForAuthenticationAsync(SignatureResponse operation, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 60;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var status = await _authorizationClient.GetAuthStatusAsync(operation.ReferenceNumber, operation.AuthenticationToken.Token, cancellationToken);

            switch (status.Status.Code)
            {
                case 200:
                    return;

                case 100:
                    if (attempt < maxAttempts) await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException($"Błąd uwierzytelniania KSeF. Kod: {status.Status.Code}, Opis: {status.Status.Description}");
            }
        }

        throw new TimeoutException("KSeF nie zakończył uwierzytelniania w wyznaczonym czasie.");
    }

    private async Task SaveAuthenticationTokensAsync(SignatureResponse operation, Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default)
    {
        var tokens = await _authorizationClient.GetAccessTokenAsync(operation.AuthenticationToken.Token, cancellationToken);

        if (string.IsNullOrWhiteSpace(tokens.AccessToken?.Token) || string.IsNullOrWhiteSpace(tokens.RefreshToken?.Token)) throw new InvalidOperationException("KSeF nie zwrócił wymaganych tokenów.");

        await _credentialService.SaveSessionTokensAsync(tenantId, userId, credentialId, nip, tokens.AccessToken.Token, tokens.AccessToken.ValidUntil, tokens.RefreshToken.Token, tokens.RefreshToken.ValidUntil, cancellationToken);
    }

    public async Task AuthenticateAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty) throw new ArgumentException("Nieprawidłowy CredentialId.", nameof(credentialId));
        if (tenantId == Guid.Empty) throw new ArgumentException("Nieprawidłowy TenantId.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Brak UserId.", nameof(userId));
        if (string.IsNullOrWhiteSpace(nip) || nip.Length != 10 || !nip.All(char.IsAsciiDigit)) throw new ArgumentException("Nieprawidłowy NIP.", nameof(nip));

        string signedXml = await PrepareSignedAuthenticationXmlAsync(credentialId, tenantId, userId, nip, cancellationToken);

        SignatureResponse operation = await SendSignedAuthenticationXmlAsync(signedXml, cancellationToken);

        await WaitForAuthenticationAsync(operation, cancellationToken);

        await SaveAuthenticationTokensAsync(operation, credentialId, tenantId, userId, nip, cancellationToken);
    }

}