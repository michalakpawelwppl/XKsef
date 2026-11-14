using System.Security.Cryptography;

namespace xKsef.CredentialService.Security;

/// <summary>AAD v2 only, for source credentials and session packages.</summary>
public sealed class ContextualSecretEncryptionService : IContextualSecretEncryptionService
{
    private readonly IKeyProtector _keyProtector;

    public ContextualSecretEncryptionService(IKeyProtector keyProtector)
    {
        ArgumentNullException.ThrowIfNull(keyProtector);
        _keyProtector = keyProtector;
    }

    public EncryptedSecret Encrypt(byte[] data, SecretEncryptionContext context)
    {
        ArgumentNullException.ThrowIfNull(data);
        ArgumentNullException.ThrowIfNull(context);
        byte[] dek = RandomNumberGenerator.GetBytes(32);
        try
        {
            var secret = new EncryptedSecret { EncryptedData = new byte[data.Length], Nonce = RandomNumberGenerator.GetBytes(12), AuthTag = new byte[16] };
            using var aes = new AesGcm(dek, 16);
            aes.Encrypt(secret.Nonce, data, secret.EncryptedData, secret.AuthTag, context.CreateAad());
            secret.EncryptedDek = _keyProtector.Protect(dek);
            return secret;
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public byte[] Decrypt(EncryptedSecret secret, SecretEncryptionContext context)
    {
        ArgumentNullException.ThrowIfNull(secret);
        ArgumentNullException.ThrowIfNull(context);
        if (secret.ProtectionType != "DPAPI-CurrentUser") throw new NotSupportedException("Unsupported key protection mechanism.");
        if (secret.EncryptedData is null || secret.EncryptedDek is null || secret.EncryptedDek.Length == 0 || secret.Nonce is null || secret.Nonce.Length != 12 || secret.AuthTag is null || secret.AuthTag.Length != 16)
            throw new CryptographicException("Invalid encrypted secret.");
        byte[] dek = _keyProtector.Unprotect(secret.EncryptedDek);
        try
        {
            if (dek.Length != 32) throw new CryptographicException("Invalid data encryption key.");
            byte[] data = new byte[secret.EncryptedData.Length];
            try
            {
                using var aes = new AesGcm(dek, 16);
                aes.Decrypt(secret.Nonce, secret.EncryptedData, secret.AuthTag, data, context.CreateAad());
                return data;
            }
            catch
            {
                CryptographicOperations.ZeroMemory(data);
                throw;
            }
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }
}
