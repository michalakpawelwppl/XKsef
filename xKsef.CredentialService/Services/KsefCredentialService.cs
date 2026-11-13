using Microsoft.EntityFrameworkCore;
using xKsef.CredentialService.Data;
using xKsef.CredentialService.Security;

namespace xKsef.CredentialService.Services;

public sealed class KsefCredentialService : IKsefCredentialService
{
    private readonly CredentialsDbContext _dbContext;
    private readonly ISecretEncryptionService _encryptionService;

    public KsefCredentialService(CredentialsDbContext dbContext, ISecretEncryptionService encryptionService)
    {
        _dbContext = dbContext;
        _encryptionService = encryptionService;
    }

    public async Task<Guid> SaveAsync(Guid tenantId, string userId, string credentialType, byte[] data, CancellationToken cancellationToken = default, DateTimeOffset? validUntil = null)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId nie może być pusty.", nameof(userId));
        if (credentialType != "Token" && credentialType != "Certificate" && credentialType != "AccessToken" && credentialType != "RefreshToken")
            throw new ArgumentException("Nieobsługiwany typ poświadczenia.", nameof(credentialType));

        if (data == null || data.Length == 0) throw new ArgumentException("Dane poświadczenia nie mogą być puste.", nameof(data));

        if (credentialType == "AccessToken" || credentialType == "RefreshToken")
        {
            KsefCredential? existingCredential = await _dbContext.KsefCredentials.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && x.CredentialType == credentialType, cancellationToken);

            if (existingCredential != null)
            {
                EncryptedSecret updated = _encryptionService.Encrypt(data, tenantId, existingCredential.Id);

                existingCredential.EncryptedData = updated.EncryptedData;
                existingCredential.EncryptedDek = updated.EncryptedDek;
                existingCredential.Nonce = updated.Nonce;
                existingCredential.AuthTag = updated.AuthTag;
                existingCredential.ProtectionType = updated.ProtectionType;
                existingCredential.ValidUntil = validUntil;

                await _dbContext.SaveChangesAsync(cancellationToken);

                return existingCredential.Id;
            }
        }

        Guid credentialId = Guid.NewGuid();
        EncryptedSecret encrypted = _encryptionService.Encrypt(data, tenantId, credentialId);

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
            ValidUntil = validUntil
        };

        _dbContext.KsefCredentials.Add(credential);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return credential.Id;
    }

    public async Task<byte[]?> GetAsync(Guid credentialId, Guid tenantId, string userId, string credentialType, CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty) throw new ArgumentException("CredentialId nie może być pusty.", nameof(credentialId));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId nie może być pusty.", nameof(userId));

        KsefCredential? credential = await _dbContext.KsefCredentials.AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == credentialId && x.TenantId == tenantId && x.UserId == userId && x.CredentialType == credentialType, cancellationToken);

        if (credential == null) return null;

        var encrypted = new EncryptedSecret
        {
            EncryptedData = credential.EncryptedData,
            EncryptedDek = credential.EncryptedDek,
            Nonce = credential.Nonce,
            AuthTag = credential.AuthTag,
            ProtectionType = credential.ProtectionType
        };

        return _encryptionService.Decrypt(encrypted, credential.TenantId, credential.Id);
    }

    public async Task<Guid?> FindCredentialIdAsync(Guid tenantId, string userId, string credentialType, CancellationToken cancellationToken = default)
    {
        return await _dbContext.KsefCredentials.AsNoTracking().Where(c => c.TenantId == tenantId && c.UserId == userId && c.CredentialType == credentialType).OrderByDescending(c => c.CreatedAt).Select(c => (Guid?)c.Id).FirstOrDefaultAsync(cancellationToken);
    }

    public async Task SaveSessionTokensAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, string accessToken, DateTimeOffset accessTokenValidUntil, string refreshToken, DateTimeOffset refreshTokenValidUntil, CancellationToken cancellationToken = default)
    {
        if (sourceCredentialId == Guid.Empty) throw new ArgumentException("SourceCredentialId nie może być pusty.", nameof(sourceCredentialId));
        if (string.IsNullOrWhiteSpace(contextNip) || contextNip.Length != 10 || !contextNip.All(char.IsAsciiDigit)) throw new ArgumentException("Nieprawidłowy NIP.", nameof(contextNip));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId nie może być pusty.", nameof(userId));
        if (string.IsNullOrWhiteSpace(accessToken)) throw new ArgumentException("AccessToken nie może być pusty.", nameof(accessToken));
        if (string.IsNullOrWhiteSpace(refreshToken)) throw new ArgumentException("RefreshToken nie może być pusty.", nameof(refreshToken));

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        await SaveTokenAsync("AccessToken", accessToken, accessTokenValidUntil);
        await SaveTokenAsync("RefreshToken", refreshToken, refreshTokenValidUntil);

        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        async Task SaveTokenAsync(string credentialType, string token, DateTimeOffset validUntil)
        {
            KsefCredential? credential = await _dbContext.KsefCredentials
                .FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && x.CredentialType == credentialType && x.SourceCredentialId == sourceCredentialId 
                    && x.ContextNip == contextNip, cancellationToken);

            if (credential == null)
            {
                credential = new KsefCredential
                {
                    Id = Guid.NewGuid(),
                    TenantId = tenantId,
                    UserId = userId,
                    CredentialType = credentialType,
                    CreatedAt = DateTimeOffset.UtcNow,
                    SourceCredentialId = sourceCredentialId,
                    ContextNip = contextNip
                };

                _dbContext.KsefCredentials.Add(credential);
            }

            byte[] tokenBytes = System.Text.Encoding.UTF8.GetBytes(token);

            try
            {
                EncryptedSecret encrypted = _encryptionService.Encrypt(tokenBytes, tenantId, credential.Id);

                credential.EncryptedData = encrypted.EncryptedData;
                credential.EncryptedDek = encrypted.EncryptedDek;
                credential.Nonce = encrypted.Nonce;
                credential.AuthTag = encrypted.AuthTag;
                credential.ProtectionType = encrypted.ProtectionType;
                credential.ValidUntil = validUntil;
                credential.SourceCredentialId = sourceCredentialId;
                credential.ContextNip = contextNip;
            }
            finally
            {
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(tokenBytes);
            }
        }
    }

    public async Task<bool> HasValidSessionAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId nie może być pusty.", nameof(userId));
        if (sourceCredentialId == Guid.Empty) throw new ArgumentException("SourceCredentialId nie może być pusty.", nameof(sourceCredentialId));
        if (string.IsNullOrWhiteSpace(contextNip) || contextNip.Length != 10 || !contextNip.All(char.IsAsciiDigit)) throw new ArgumentException("Nieprawidłowy NIP.", nameof(contextNip));

        DateTimeOffset minimumValidity = DateTimeOffset.UtcNow.AddMinutes(2);

        return await _dbContext.KsefCredentials.AsNoTracking().AnyAsync(x => x.TenantId == tenantId && x.UserId == userId && x.CredentialType == "AccessToken" && x.SourceCredentialId == sourceCredentialId && x.ContextNip == contextNip && x.ValidUntil > minimumValidity, cancellationToken);
    }

    public async Task<StoredSessionToken?> GetRefreshTokenAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId nie może być pusty.", nameof(userId));
        if (sourceCredentialId == Guid.Empty) throw new ArgumentException("SourceCredentialId nie może być pusty.", nameof(sourceCredentialId));
        if (string.IsNullOrWhiteSpace(contextNip) || contextNip.Length != 10 || !contextNip.All(char.IsAsciiDigit)) throw new ArgumentException("Nieprawidłowy NIP.", nameof(contextNip));

        KsefCredential? credential = await _dbContext.KsefCredentials.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && x.CredentialType == "RefreshToken" && x.SourceCredentialId == sourceCredentialId && x.ContextNip == contextNip, cancellationToken);

        if (credential == null || !credential.ValidUntil.HasValue) return null;

        var encrypted = new EncryptedSecret
        {
            EncryptedData = credential.EncryptedData,
            EncryptedDek = credential.EncryptedDek,
            Nonce = credential.Nonce,
            AuthTag = credential.AuthTag,
            ProtectionType = credential.ProtectionType
        };

        byte[] tokenBytes = _encryptionService.Decrypt(encrypted, credential.TenantId, credential.Id);

        return new StoredSessionToken(tokenBytes, credential.ValidUntil.Value);
    }

    public async Task SaveAccessTokenAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, string accessToken, DateTimeOffset validUntil, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId nie może być pusty.", nameof(userId));
        if (sourceCredentialId == Guid.Empty) throw new ArgumentException("SourceCredentialId nie może być pusty.", nameof(sourceCredentialId));
        if (string.IsNullOrWhiteSpace(contextNip) || contextNip.Length != 10 || !contextNip.All(char.IsAsciiDigit)) throw new ArgumentException("Nieprawidłowy NIP.", nameof(contextNip));
        if (string.IsNullOrWhiteSpace(accessToken)) throw new ArgumentException("AccessToken nie może być pusty.", nameof(accessToken));

        KsefCredential? credential = await _dbContext.KsefCredentials.FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && x.CredentialType == "AccessToken" && x.SourceCredentialId == sourceCredentialId && x.ContextNip == contextNip, cancellationToken);

        if (credential == null)
        {
            credential = new KsefCredential
            {
                Id = Guid.NewGuid(),
                TenantId = tenantId,
                UserId = userId,
                CredentialType = "AccessToken",
                CreatedAt = DateTimeOffset.UtcNow
            };

            _dbContext.KsefCredentials.Add(credential);
        }

        byte[] tokenBytes = System.Text.Encoding.UTF8.GetBytes(accessToken);

        try
        {
            EncryptedSecret encrypted = _encryptionService.Encrypt(tokenBytes, tenantId, credential.Id);

            credential.EncryptedData = encrypted.EncryptedData;
            credential.EncryptedDek = encrypted.EncryptedDek;
            credential.Nonce = encrypted.Nonce;
            credential.AuthTag = encrypted.AuthTag;
            credential.ProtectionType = encrypted.ProtectionType;
            credential.ValidUntil = validUntil;
            credential.SourceCredentialId = sourceCredentialId;
            credential.ContextNip = contextNip;

            await _dbContext.SaveChangesAsync(cancellationToken);
        }
        finally
        {
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(tokenBytes);
        }
    }

    public async Task<StoredSessionToken?> GetAccessTokenAsync(Guid tenantId, string userId, Guid sourceCredentialId, string contextNip, CancellationToken cancellationToken = default)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId nie może być pusty.", nameof(userId));
        if (sourceCredentialId == Guid.Empty) throw new ArgumentException("SourceCredentialId nie może być pusty.", nameof(sourceCredentialId));
        if (string.IsNullOrWhiteSpace(contextNip) || contextNip.Length != 10 || !contextNip.All(char.IsAsciiDigit)) throw new ArgumentException("Nieprawidłowy NIP.", nameof(contextNip));

        KsefCredential? credential = await _dbContext.KsefCredentials.AsNoTracking().FirstOrDefaultAsync(x => x.TenantId == tenantId && x.UserId == userId && x.CredentialType == "AccessToken" && x.SourceCredentialId == sourceCredentialId && x.ContextNip == contextNip, cancellationToken);

        if (credential == null || !credential.ValidUntil.HasValue) return null;

        var encrypted = new EncryptedSecret
        {
            EncryptedData = credential.EncryptedData,
            EncryptedDek = credential.EncryptedDek,
            Nonce = credential.Nonce,
            AuthTag = credential.AuthTag,
            ProtectionType = credential.ProtectionType
        };

        byte[] tokenBytes = _encryptionService.Decrypt(encrypted, credential.TenantId, credential.Id);

        return new StoredSessionToken(tokenBytes, credential.ValidUntil.Value);
    }

    public async Task<string?> GetCredentialTypeAsync(Guid credentialId, Guid tenantId, string userId, CancellationToken cancellationToken = default)
    {
        if (credentialId == Guid.Empty) throw new ArgumentException("CredentialId nie może być pusty.", nameof(credentialId));
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        if (string.IsNullOrWhiteSpace(userId)) throw new ArgumentException("UserId nie może być pusty.", nameof(userId));

        return await _dbContext.KsefCredentials.AsNoTracking().Where(x => x.Id == credentialId && x.TenantId == tenantId && x.UserId == userId && (x.CredentialType == "Token" || x.CredentialType == "Certificate")).Select(x => x.CredentialType).FirstOrDefaultAsync(cancellationToken);
    }

}