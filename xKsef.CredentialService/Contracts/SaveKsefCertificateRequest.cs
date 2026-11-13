namespace xKsef.CredentialService.Models;

public sealed record SaveKsefCertificateRequest(Guid TenantId, string UserId, string CertificatePem, string EncryptedPrivateKeyPem, string Password);