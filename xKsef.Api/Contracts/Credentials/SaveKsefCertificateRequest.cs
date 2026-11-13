namespace xKsef.Api.Contracts;

public sealed record SaveKsefCertificateRequest(string CertificatePem, string EncryptedPrivateKeyPem, string Password);