namespace xKsef.CredentialService.Security;

public interface IContextualSecretEncryptionService
{
    // Input buffers remain owned by the caller, who must clear them when no longer needed.
    EncryptedSecret Encrypt(byte[] data, SecretEncryptionContext context);
    byte[] Decrypt(EncryptedSecret secret, SecretEncryptionContext context);
}
