using System.Security.Cryptography;
using System.Text;

namespace xKsef.CredentialService.Security;

public sealed class SecretEncryptionService : ISecretEncryptionService
{
    private readonly IKeyProtector _keyProtector;

    public SecretEncryptionService(IKeyProtector keyProtector)
    {
        _keyProtector = keyProtector;
    }

    public EncryptedSecret Encrypt(byte[] data, Guid tenantId, Guid credentialId)
    {
        ArgumentNullException.ThrowIfNull(data);
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        if (credentialId == Guid.Empty) throw new ArgumentException("CredentialId nie może być pusty.", nameof(credentialId));

        byte[] dek = RandomNumberGenerator.GetBytes(32);

        try
        {
            byte[] nonce = RandomNumberGenerator.GetBytes(12);
            byte[] encryptedData = new byte[data.Length];
            byte[] authTag = new byte[16];
            byte[] aad = CreateAad(tenantId, credentialId);

            using (var aes = new AesGcm(dek, 16))
            {
                aes.Encrypt(nonce, data, encryptedData, authTag, aad);
            }

            byte[] encryptedDek = _keyProtector.Protect(dek);

            return new EncryptedSecret
            {
                EncryptedData = encryptedData,
                EncryptedDek = encryptedDek,
                Nonce = nonce,
                AuthTag = authTag
            };
        }
        finally
        {
            CryptographicOperations.ZeroMemory(dek);
        }
    }

    public byte[] Decrypt(EncryptedSecret secret, Guid tenantId, Guid credentialId)
    {
        ArgumentNullException.ThrowIfNull(secret);
        if (tenantId == Guid.Empty) throw new ArgumentException("TenantId nie może być pusty.", nameof(tenantId));
        if (credentialId == Guid.Empty) throw new ArgumentException("CredentialId nie może być pusty.", nameof(credentialId));
        if (secret.ProtectionType != "DPAPI-CurrentUser") throw new NotSupportedException($"Nieobsługiwany mechanizm ochrony: {secret.ProtectionType}");

        byte[] dek = _keyProtector.Unprotect(secret.EncryptedDek);

        try
        {
            byte[] data = new byte[secret.EncryptedData.Length];
            byte[] aad = CreateAad(tenantId, credentialId);

            try
            {
                using (var aes = new AesGcm(dek, 16))
                {
                    aes.Decrypt(secret.Nonce, secret.EncryptedData, secret.AuthTag, data, aad);
                }

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

    private static byte[] CreateAad(Guid tenantId, Guid credentialId)
    {
        return Encoding.UTF8.GetBytes($"xKsef:v1:{tenantId:D}:{credentialId:D}");
    }
}