
using System.Security.Cryptography.X509Certificates;
using KSeF.Client.Api.Builders.Auth;
using KSeF.Client.Api.Services;
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

    public async Task<AuthenticationOperation> StartAuthenticationAsync(string nip, string certificatePath, string certificatePassword, CancellationToken cancellationToken = default)
    {
        // 1. Pobranie wyzwania z KSeF
        var challenge = await _authorizationClient.GetAuthChallengeAsync(cancellationToken);

        // 2. Przygotowanie żądania uwierzytelniania
        var request = AuthTokenRequestBuilder.Create().WithChallenge(challenge.Challenge)
            .WithContext(AuthenticationTokenContextIdentifierType.Nip, nip)
            .WithIdentifierType(AuthenticationTokenSubjectIdentifierTypeEnum.CertificateSubject)
            .Build();

        // 3. Serializacja do XML
        string xml = request.SerializeToXmlString();

        // 4. Wczytanie certyfikatu z kluczem prywatnym
        using var certificate = X509CertificateLoader
            .LoadPkcs12FromFile(
                certificatePath,
                certificatePassword);

        if (!certificate.HasPrivateKey)
        {
            throw new InvalidOperationException(
                "Certyfikat nie zawiera klucza prywatnego.");
        }

        // 5. Podpis XAdES
        // 5. Podpis XAdES
        string signedXml = SignatureService.Sign(xml, certificate);

        // 6. Wysłanie podpisanego XML do KSeF
        var operation = await _authorizationClient
            .SubmitXadesAuthRequestAsync(
                signedXml,
                verifyCertificateChain: false,
                enforceXadesCompliance: false,
                cancellationToken: cancellationToken);

        // 7. Na razie zwracamy identyfikator operacji
        return new AuthenticationOperation
        {
            ReferenceNumber = operation.ReferenceNumber,
            AuthenticationToken = operation.AuthenticationToken.Token
        };
    }


    public async Task CompleteAuthenticationAsync(AuthenticationOperation operation, CancellationToken cancellationToken = default)
    {
        const int maxAttempts = 60;

        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var status = await _authorizationClient.GetAuthStatusAsync(
                operation.ReferenceNumber,
                operation.AuthenticationToken,
                cancellationToken);

            switch (status.Status.Code)
            {
                case 200:
                    // Uwierzytelnianie zakończone sukcesem.
                    var tokens = await _authorizationClient
                        .GetAccessTokenAsync(
                            operation.AuthenticationToken,
                            cancellationToken);

                    if (string.IsNullOrWhiteSpace(tokens.AccessToken?.Token) || string.IsNullOrWhiteSpace(tokens.RefreshToken?.Token))
                    {
                        throw new InvalidOperationException(
                            "KSeF nie zwrócił wymaganych tokenów.");
                    }

                    _tokenStore.Set(new KsefTokens(
                        AccessToken: tokens.AccessToken.Token,
                        AccessTokenValidUntil: tokens.AccessToken.ValidUntil,
                        RefreshToken: tokens.RefreshToken.Token,
                        RefreshTokenValidUntil: tokens.RefreshToken.ValidUntil
                    ));

                    return;

                    // Na razie potwierdzamy otrzymanie tokenów.
                    // Nie zapisujemy ich ani nie wyświetlamy.
                    return;

                case 100:
                    // KSeF nadal przetwarza uwierzytelnianie.
                    if (attempt < maxAttempts)
                    {
                        await Task.Delay(
                            TimeSpan.FromSeconds(2),
                            cancellationToken);
                    }
                    break;

                default:
                    throw new InvalidOperationException(
                        $"Błąd uwierzytelniania KSeF. " +
                        $"Kod: {status.Status.Code}, " +
                        $"Opis: {status.Status.Description}");
            }
        }

        throw new TimeoutException(
            "KSeF nie zakończył uwierzytelniania w ciągu 120 sekund.");
    }

    public async Task AuthenticateAsync(
    string nip,
    string certificatePath,
    string certificatePassword,
    CancellationToken cancellationToken = default)
    {
        var operation = await StartAuthenticationAsync(
            nip,
            certificatePath,
            certificatePassword,
            cancellationToken);

        await CompleteAuthenticationAsync(
            operation,
            cancellationToken);
    }

}
