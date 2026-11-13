namespace xKsef.CredentialService.Security;

public interface ISecretEncryptionService
{
    EncryptedSecret Encrypt(byte[] data, Guid tenantId, Guid credentialId);
    byte[] Decrypt(EncryptedSecret secret, Guid tenantId, Guid credentialId);
}