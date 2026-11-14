using xKsef.CredentialService.Contracts;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using xKsef.CredentialService.Data;
using xKsef.CredentialService.Security;

namespace xKsef.CredentialService.Services;

public sealed class KsefCredentialService : IKsefCredentialService
{
    private readonly CredentialsDbContext _dbContext;
    private readonly IContextualSecretEncryptionService _encryptionService;

    public KsefCredentialService(CredentialsDbContext dbContext, IContextualSecretEncryptionService encryptionService)
    {
        _dbContext = dbContext;
        _encryptionService = encryptionService;
    }

    public async Task<Guid> SaveAsync(Guid? tenantId, string userId, string credentialType, byte[] data, CancellationToken cancellationToken = default, string? contextNip = null)
    {
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId nie może być pusty.", nameof(userId));
        if (credentialType != "Token" && credentialType != "Certificate")
            throw new ArgumentException("Nieobsługiwany typ poświadczenia.", nameof(credentialType));

        if (data == null || data.Length == 0) throw new ArgumentException("Dane poświadczenia nie mogą być puste.", nameof(data));

        ValidateSourceContext(credentialType, tenantId, contextNip);

        Guid credentialId = Guid.NewGuid();
        EncryptedSecret encrypted = _encryptionService.Encrypt(data, SourceAad(credentialType, credentialId, userId, tenantId, contextNip));

        var credential = new KsefCredential
        {
            Id = credentialId,
            TenantId = tenantId,
            UserId = userId,
            CredentialType = credentialType,
            EncryptedData = encrypted.EncryptedData,
            EncryptedDek = encrypted.EncryptedDek,
            Nonce = encrypted.Nonce,
            AuthTag = encrypted.AuthTag,
            ProtectionType = encrypted.ProtectionType,
            CreatedAt = DateTimeOffset.UtcNow,
            ContextNip = credentialType == "Token" ? contextNip : null
        };

        _dbContext.KsefCredentials.Add(credential);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return credential.Id;
    }

    public async Task<byte[]?> GetAsync(Guid credentialId, Guid? tenantId, string userId, string credentialType, CancellationToken cancellationToken = default, string? contextNip = null)
    {
        if (credentialId == Guid.Empty) throw new ArgumentException("CredentialId nie może być pusty.", nameof(credentialId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId nie może być pusty.", nameof(userId));

        ValidateSourceContext(credentialType, tenantId, contextNip);

        KsefCredential? credential = await _dbContext.KsefCredentials.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == credentialId && x.TenantId == tenantId && x.UserId == userId && x.CredentialType == credentialType, cancellationToken);

        if (credential == null || !credential.IsActive || (credentialType == "Token" && !string.Equals(credential.ContextNip, contextNip, StringComparison.Ordinal))) return null;

        var encrypted = new EncryptedSecret
        {
            EncryptedData = credential.EncryptedData,
            EncryptedDek = credential.EncryptedDek,
            Nonce = credential.Nonce,
            AuthTag = credential.AuthTag,
            ProtectionType = credential.ProtectionType
        };

        return _encryptionService.Decrypt(encrypted, SourceAad(credential.CredentialType, credential.Id, credential.UserId, credential.TenantId, credential.ContextNip));
    }

    public async Task<Guid?> FindCredentialIdAsync(Guid? tenantId, string userId, string credentialType, CancellationToken cancellationToken = default, string? contextNip = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        ValidateSourceContext(credentialType, tenantId, contextNip);
        var credential = await _dbContext.KsefCredentials.AsNoTracking()
            .Where(c => c.TenantId == tenantId && c.UserId == userId && c.CredentialType == credentialType)
            .OrderByDescending(c => c.CreatedAt).Select(c => new { c.Id, c.ContextNip, c.IsActive }).FirstOrDefaultAsync(cancellationToken);
        // Validate the selected credential; never fall back to an older token from another context.
        if (credential is null || !credential.IsActive || (credentialType == "Token" && !string.Equals(credential.ContextNip, contextNip, StringComparison.Ordinal))) return null;
        return credential.Id;
    }

    public async Task SaveSessionTokensAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, string accessToken, DateTimeOffset accessTokenValidUntil, string refreshToken, DateTimeOffset refreshTokenValidUntil, CancellationToken cancellationToken = default)
    {
        ValidateSessionContext(tenantId, userId, sourceCredentialId, contextNip);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshToken);
        if (!await HasSourceContextAsync(tenantId, userId, sourceCredentialId, contextNip, cancellationToken))
            throw new InvalidOperationException("Source credential is inactive or its context does not match.");

        var session = await SessionQuery(tenantId, userId, sourceCredentialId, contextNip).FirstOrDefaultAsync(cancellationToken);
        using var package = SessionTokenPackage.Create(accessToken, accessTokenValidUntil, refreshToken, refreshTokenValidUntil);
        if (session is null)
        {
            var created = new KsefSession
            {
                Id = Guid.NewGuid(), UserId = userId, TenantId = tenantId, CredentialId = sourceCredentialId, ContextNip = contextNip,
                AccessTokenValidUntil = accessTokenValidUntil, RefreshTokenValidUntil = refreshTokenValidUntil
            };
            var encrypted = EncryptPackage(created, package);
            created.EncryptedData = encrypted.EncryptedData;
            created.EncryptedDek = encrypted.EncryptedDek;
            created.Nonce = encrypted.Nonce;
            created.AuthTag = encrypted.AuthTag;
            created.ProtectionType = encrypted.ProtectionType;
            _dbContext.KsefSessions.Add(created);
            try
            {
                // One INSERT holds the entire pair. The unique context index rejects concurrent creation.
                await _dbContext.SaveChangesAsync(cancellationToken);
            }
            finally
            {
                _dbContext.Entry(created).State = EntityState.Detached;
            }
        }
        else
        {
            await ReplacePackageAsync(session, package, cancellationToken);
        }
    }

    public async Task<bool> HasValidSessionAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, CancellationToken cancellationToken = default)
    {
        StoredSessionToken? token = await GetAccessTokenAsync(tenantId, userId, sourceCredentialId, contextNip, cancellationToken);
        if (token is null) return false;
        try
        {
            return token.ValidUntil > DateTimeOffset.UtcNow.AddMinutes(2);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(token.TokenBytes);
        }
    }

    public async Task<StoredSessionToken?> GetRefreshTokenAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, CancellationToken cancellationToken = default)
    {
        ValidateSessionContext(tenantId, userId, sourceCredentialId, contextNip);
        var session = await SessionQuery(tenantId, userId, sourceCredentialId, contextNip).FirstOrDefaultAsync(cancellationToken);
        if (session is null) return null;
        using var package = DecryptPackage(session);
        return new(package.RefreshToken.ToArray(), package.RefreshTokenValidUntil);
    }

    public async Task SaveAccessTokenAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, string accessToken, DateTimeOffset validUntil, CancellationToken cancellationToken = default)
    {
        ValidateSessionContext(tenantId, userId, sourceCredentialId, contextNip);
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);
        var session = await SessionQuery(tenantId, userId, sourceCredentialId, contextNip).FirstOrDefaultAsync(cancellationToken);
        if (session is null) throw new InvalidOperationException("No authorized session exists to refresh.");
        using var previous = DecryptPackage(session);
        if (previous.RefreshTokenValidUntil <= DateTimeOffset.UtcNow.AddMinutes(2))
            throw new InvalidOperationException("RefreshToken has expired or is about to expire.");
        using var updated = new SessionTokenPackage(Encoding.UTF8.GetBytes(accessToken), validUntil, previous.RefreshToken.ToArray(), previous.RefreshTokenValidUntil);
        await ReplacePackageAsync(session, updated, cancellationToken);
    }

    public async Task<StoredSessionToken?> GetAccessTokenAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, CancellationToken cancellationToken = default)
    {
        ValidateSessionContext(tenantId, userId, sourceCredentialId, contextNip);
        var session = await SessionQuery(tenantId, userId, sourceCredentialId, contextNip).FirstOrDefaultAsync(cancellationToken);
        if (session is null) return null;
        using var package = DecryptPackage(session);
        return new(package.AccessToken.ToArray(), package.AccessTokenValidUntil);
    }

    private IQueryable<KsefSession> SessionQuery(Guid tenantId, string userId, Guid credentialId, string contextNip) =>
        _dbContext.KsefSessions.AsNoTracking().Where(session => session.TenantId == tenantId && session.UserId == userId &&
            session.CredentialId == credentialId && session.ContextNip == contextNip &&
            _dbContext.KsefCredentials.Any(source => source.Id == credentialId && source.UserId == userId &&
                source.IsActive && ((source.CredentialType == "Certificate" && source.TenantId == null && source.ContextNip == null) ||
                    (source.CredentialType == "Token" && source.TenantId == tenantId && source.ContextNip == contextNip))));

    private async Task ReplacePackageAsync(KsefSession previous, SessionTokenPackage package, CancellationToken cancellationToken)
    {
        var encrypted = EncryptPackage(previous, package);
        // Compare-and-swap in one SQL UPDATE, including a fresh source authorization check.
        // Random nonce and ciphertext identify the version read; a stale writer cannot overwrite a newer package.
        int affected = await SessionQuery(previous.TenantId, previous.UserId, previous.CredentialId, previous.ContextNip)
            .Where(session => session.Id == previous.Id && session.Nonce == previous.Nonce && session.EncryptedData == previous.EncryptedData &&
                session.EncryptedDek == previous.EncryptedDek && session.AuthTag == previous.AuthTag)
            .ExecuteUpdateAsync(setters => setters
                .SetProperty(session => session.EncryptedData, encrypted.EncryptedData)
                .SetProperty(session => session.EncryptedDek, encrypted.EncryptedDek)
                .SetProperty(session => session.Nonce, encrypted.Nonce)
                .SetProperty(session => session.AuthTag, encrypted.AuthTag)
                .SetProperty(session => session.ProtectionType, encrypted.ProtectionType)
                .SetProperty(session => session.AccessTokenValidUntil, package.AccessTokenValidUntil)
                .SetProperty(session => session.RefreshTokenValidUntil, package.RefreshTokenValidUntil), cancellationToken);
        if (affected != 1) throw new DbUpdateConcurrencyException("Session changed or source authorization was revoked.");
    }

    private EncryptedSecret EncryptPackage(KsefSession session, SessionTokenPackage package)
    {
        byte[] data = package.Serialize();
        try
        {
            return _encryptionService.Encrypt(data, SessionAad(session));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }

    private SessionTokenPackage DecryptPackage(KsefSession session)
    {
        byte[] data = _encryptionService.Decrypt(new EncryptedSecret
        {
            EncryptedData = session.EncryptedData, EncryptedDek = session.EncryptedDek, Nonce = session.Nonce,
            AuthTag = session.AuthTag, ProtectionType = session.ProtectionType
        }, SessionAad(session));
        try
        {
            var package = SessionTokenPackage.Deserialize(data);
            if (!package.AccessTokenValidUntil.EqualsExact(session.AccessTokenValidUntil) || !package.RefreshTokenValidUntil.EqualsExact(session.RefreshTokenValidUntil))
            {
                package.Dispose();
                throw new CryptographicException("Session expiration metadata does not match its authenticated package.");
            }
            return package;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(data);
        }
    }

    private static SecretEncryptionContext SessionAad(KsefSession session) =>
        SecretEncryptionContext.KsefSession(session.Id, session.UserId, session.TenantId, session.CredentialId, session.ContextNip);

    private static void ValidateSessionContext(Guid tenantId, string userId, Guid credentialId, string contextNip)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId is required.", nameof(tenantId));
        if (credentialId == Guid.Empty) throw new ArgumentException("CredentialId is required.", nameof(credentialId));
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        if (contextNip is null || contextNip.Length != 10 || !contextNip.All(char.IsAsciiDigit))
            throw new ArgumentException("Context NIP must contain exactly ten ASCII digits.", nameof(contextNip));
    }

    public async Task<IReadOnlyList<KsefCertificateInfo>> GetCertificatesAsync(string userId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(userId);
        return await _dbContext.KsefCredentials.AsNoTracking()
            .Where(c => c.UserId == userId && c.IsActive && c.CredentialType == "Certificate" && c.TenantId == null && c.ContextNip == null)
            .OrderByDescending(c => c.CreatedAt).ThenBy(c => c.Id)
            .Select(c => new KsefCertificateInfo(c.Id, c.Name, c.CreatedAt)).ToListAsync(cancellationToken);
    }

    public async Task<string?> GetCredentialTypeAsync(Guid credentialId, Guid tenantId, string userId, string contextNip, CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty) throw new ArgumentException("CredentialId nie może być pusty.", nameof(credentialId));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId nie może być pusty.", nameof(userId));

        ValidateSessionContext(tenantId, userId, credentialId, contextNip);
        return await _dbContext.KsefCredentials.AsNoTracking().Where(x => x.Id == credentialId && x.UserId == userId && x.IsActive && ((x.CredentialType == "Token" && x.TenantId == tenantId && x.ContextNip == contextNip) || (x.CredentialType == "Certificate" && x.TenantId == null && x.ContextNip == null))).Select(x => x.CredentialType).FirstOrDefaultAsync(cancellationToken);
    }

    private async Task<bool> HasSourceContextAsync(Guid tenantId, string userId, Guid credentialId, string contextNip, CancellationToken cancellationToken)
    {
        var source = await _dbContext.KsefCredentials.AsNoTracking()
            .Where(x => x.Id == credentialId && x.UserId == userId)
            .Select(x => new { x.CredentialType, x.ContextNip, x.IsActive, x.TenantId }).FirstOrDefaultAsync(cancellationToken);
        return source is not null && source.IsActive && ((source.CredentialType == "Certificate" && source.TenantId is null && source.ContextNip is null) ||
            (source.CredentialType == "Token" && source.TenantId == tenantId && string.Equals(source.ContextNip, contextNip, StringComparison.Ordinal)));
    }

    private static void ValidateSourceContext(string credentialType, Guid? tenantId, string? contextNip)
    {
        if (credentialType != "Certificate" && credentialType != "Token")
            throw new ArgumentException("Only source credentials are supported.", nameof(credentialType));
        if (credentialType == "Certificate" && (tenantId is not null || contextNip is not null))
            throw new ArgumentException("A user certificate cannot have a tenant or NIP context.");
        if (credentialType == "Token" && (tenantId is null || tenantId == Guid.Empty))
            throw new ArgumentException("A token requires a tenant.", nameof(tenantId));
        if (credentialType == "Token" && (contextNip is null || contextNip.Length != 10 || !contextNip.All(char.IsAsciiDigit)))
            throw new ArgumentException("Context NIP must contain exactly ten ASCII digits.", nameof(contextNip));
    }
    private static SecretEncryptionContext SourceAad(string credentialType, Guid credentialId, string userId, Guid? tenantId, string? contextNip)
    {
        ValidateSourceContext(credentialType, tenantId, contextNip);
        return credentialType == "Certificate"
            ? SecretEncryptionContext.UserCertificate(credentialId, userId)
            : SecretEncryptionContext.KsefToken(credentialId, userId, tenantId!.Value, contextNip!);
    }
}