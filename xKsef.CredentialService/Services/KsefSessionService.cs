namespace xKsef.CredentialService.Services;

public sealed class KsefSessionService : IKsefSessionService
{
    private readonly IKsefCredentialService _credentialService;
    private readonly IKsefCertificateAuthenticationService _certificateAuthenticationService;
    private readonly IKsefTokenAuthenticationService _tokenAuthenticationService;
    private readonly KsefAuthenticationLock _authenticationLock;
    private readonly KsefTokenRefreshService _tokenRefreshService;

    public KsefSessionService(IKsefCredentialService credentialService, IKsefCertificateAuthenticationService certificateAuthenticationService, IKsefTokenAuthenticationService tokenAuthenticationService, KsefAuthenticationLock authenticationLock, KsefTokenRefreshService tokenRefreshService)
    {
        _credentialService = credentialService;
        _certificateAuthenticationService = certificateAuthenticationService;
        _tokenAuthenticationService = tokenAuthenticationService;
        _authenticationLock = authenticationLock;
        _tokenRefreshService = tokenRefreshService;
    }

    public async Task EnsureAuthenticatedAsync(Guid credentialId, Guid tenantId, string userId, string nip, CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty) throw new ArgumentException("Nieprawidłowy CredentialId.", nameof(credentialId));
        if (tenantId == Guid.Empty) throw new ArgumentException("Nieprawidłowy TenantId.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Nieprawidłowy UserId.", nameof(userId));
        if (string.IsNullOrWhiteSpace(nip) || nip.Length != 10 || !nip.All(char.IsAsciiDigit)) throw new ArgumentException("Nieprawidłowy NIP.", nameof(nip));

        SemaphoreSlim semaphore = _authenticationLock.GetLock(tenantId, userId);
        await semaphore.WaitAsync(cancellationToken);

        try
        {
            string? credentialType = await _credentialService.GetCredentialTypeAsync(credentialId, tenantId, userId, cancellationToken);

            if (credentialType == null) throw new InvalidOperationException("Nie znaleziono poświadczenia KSeF dla użytkownika i tenanta.");

            if (await _credentialService.HasValidSessionAsync(tenantId, userId, credentialId, nip, cancellationToken)) return;

            if (await _tokenRefreshService.TryRefreshAccessTokenAsync(tenantId, userId, credentialId, nip, cancellationToken)) return;

            switch (credentialType)
            {
                case "Token":
                    await _tokenAuthenticationService.AuthenticateCoreAsync(credentialId, tenantId, userId, nip, cancellationToken);
                    break;

                case "Certificate":
                    await _certificateAuthenticationService.AuthenticateAsync(credentialId, tenantId, userId, nip, cancellationToken);
                    break;

                default:
                    throw new InvalidOperationException($"Nieobsługiwany typ poświadczenia KSeF: {credentialType}");
            }

            if (!await _credentialService.HasValidSessionAsync(tenantId, userId, credentialId, nip, cancellationToken)) throw new InvalidOperationException("Uwierzytelnianie KSeF zakończyło się bez utworzenia ważnej sesji.");
        }
        finally
        {
            semaphore.Release();
        }
    }
}