namespace xKsef.CredentialService.Models;

public sealed record SaveKsefCertificateRequest(string UserId, string CertificatePem, string EncryptedPrivateKeyPem, string Password);