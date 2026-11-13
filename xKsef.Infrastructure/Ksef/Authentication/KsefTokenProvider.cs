using xKsef.Application.Credentials;
using xKsef.Application.Tenants;

namespace xKsef.Infrastructure.Ksef.Authentication;

public sealed class KsefTokenProvider
{
    private readonly ICredentialServiceClient _credentialServiceClient;
    private readonly ITenantService _tenantService;


    private static readonly TimeSpan ExpirationMargin =
        TimeSpan.FromMinutes(2);

    public KsefTokenProvider(ICredentialServiceClient credentialServiceClient, ITenantService tenantService)
    {
        _credentialServiceClient = credentialServiceClient;
        _tenantService = tenantService;
    }

    public async Task<string> GetAccessTokenAsync(string userId, Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("Nieprawidłowy UserId.", nameof(userId));
        if (tenantId == Guid.Empty) throw new ArgumentException("Nieprawidłowy TenantId.", nameof(tenantId));

        var taxEntity = await _tenantService.GetTaxEntityAsync(tenantId, cancellationToken);
        if (taxEntity == null) throw new InvalidOperationException("Nie znaleziono podmiotu podatkowego dla wybranego tenanta.");

        Guid? credentialId = await _tenantService.GetPreferredKsefCredentialIdAsync(tenantId, userId, cancellationToken);

        if (!credentialId.HasValue || credentialId.Value == Guid.Empty) throw new InvalidOperationException("Nie wybrano poświadczenia KSeF dla bieżącego użytkownika i tenanta.");

        await _credentialServiceClient.EnsureAuthenticatedAsync(credentialId.Value, tenantId, userId, taxEntity.Nip, cancellationToken);

        KsefAccessTokenResponse response = await _credentialServiceClient.GetAccessTokenAsync(credentialId.Value, tenantId, userId, taxEntity.Nip, cancellationToken);

        if (string.IsNullOrWhiteSpace(response.AccessToken)) throw new InvalidOperationException("CredentialService nie zwrócił AccessToken.");
        if (response.ValidUntil <= DateTimeOffset.UtcNow.AddMinutes(2)) throw new InvalidOperationException("Otrzymany AccessToken ma niewystarczający okres ważności.");

        return response.AccessToken;
    }

}