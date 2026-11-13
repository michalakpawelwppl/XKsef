using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace xKsef.CredentialService.Services;

public sealed class KsefCertificateService
{
    private readonly KsefCertificateImporter _certificateImporter;
    private readonly IKsefCredentialService _credentialService;

    public KsefCertificateService(KsefCertificateImporter certificateImporter, IKsefCredentialService credentialService)
    {
        _certificateImporter = certificateImporter;
        _credentialService = credentialService;
    }

    public async Task<Guid> SaveCertificateAsync(Guid tenantId, string userId, string certificatePem, string encryptedPrivateKeyPem, string password, CancellationToken cancellationToken = default)
    {
        using var certificate = _certificateImporter.Import(certificatePem, encryptedPrivateKeyPem, password);

        byte[] pkcs12 = _certificateImporter.ExportPkcs12(certificate);

        try
        {
            return await _credentialService.SaveAsync(tenantId, userId, "Certificate", pkcs12, cancellationToken);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs12);
        }
    }

    public async Task<X509Certificate2?> GetCertificateAsync(Guid credentialId, Guid tenantId, string userId, CancellationToken cancellationToken = default)
    {
        byte[]? pkcs12 = await _credentialService.GetAsync(credentialId, tenantId, userId, "Certificate", cancellationToken);

        if (pkcs12 == null) return null;

        try
        {
            return X509CertificateLoader.LoadPkcs12(pkcs12, null, X509KeyStorageFlags.EphemeralKeySet);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(pkcs12);
        }
    }
}