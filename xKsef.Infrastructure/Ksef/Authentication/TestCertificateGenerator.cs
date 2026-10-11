
using KSeF.Client.Api.Builders.X509Certificates;
using System.Security.Cryptography.X509Certificates;

namespace xKsef.Infrastructure.Ksef.Authentication;

public static class TestCertificateGenerator
{
    public static X509Certificate2 Generate(string nip)
    {
        if (string.IsNullOrWhiteSpace(nip) ||
            nip.Length != 10 ||
            !nip.All(char.IsDigit))
        {
            throw new ArgumentException(
                "NIP musi składać się z 10 cyfr.");
        }

        return SelfSignedCertificateForSignatureBuilder
            .Create()
            .WithGivenName("Jan")
            .WithSurname("Kowalski")
            .WithSerialNumber($"TINPL-{nip}")
            .WithCommonName("Jan Kowalski")
            .Build();
    }

    public static void Save(
        X509Certificate2 certificate,
        string path,
        string password)
    {
        if (!certificate.HasPrivateKey)
            throw new InvalidOperationException(
                "Certyfikat nie zawiera klucza prywatnego.");

        byte[] pfx = certificate.Export(
            X509ContentType.Pfx,
            password);

        File.WriteAllBytes(path, pfx);
    }
}
