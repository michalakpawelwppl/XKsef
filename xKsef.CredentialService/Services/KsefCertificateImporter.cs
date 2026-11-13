using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace xKsef.CredentialService.Services;

public sealed class KsefCertificateImporter
{
    public X509Certificate2 Import(string certificatePem, string encryptedPrivateKeyPem, string password)
    {
        if (string.IsNullOrWhiteSpace(certificatePem)) throw new ArgumentException("Brak certyfikatu.", nameof(certificatePem));
        if (string.IsNullOrWhiteSpace(encryptedPrivateKeyPem)) throw new ArgumentException("Brak klucza prywatnego.", nameof(encryptedPrivateKeyPem));
        if (string.IsNullOrEmpty(password)) throw new ArgumentException("Brak hasła do klucza prywatnego.", nameof(password));

        try
        {
            X509Certificate2 certificate = X509Certificate2.CreateFromEncryptedPem(certificatePem, encryptedPrivateKeyPem, password);

            if (!certificate.HasPrivateKey)
            {
                certificate.Dispose();
                throw new CryptographicException("Certyfikat nie posiada klucza prywatnego.");
            }

            return certificate;
        }
        catch (CryptographicException ex)
        {
            throw new InvalidOperationException("Nie udało się wczytać certyfikatu i klucza prywatnego. Sprawdź hasło oraz poprawność plików.", ex);
        }
    }

    public byte[] ExportPkcs12(X509Certificate2 certificate)
    {
        if (certificate == null) throw new ArgumentNullException(nameof(certificate));
        if (!certificate.HasPrivateKey) throw new InvalidOperationException("Certyfikat nie zawiera klucza prywatnego.");

        return certificate.Export(X509ContentType.Pkcs12);
    }
}