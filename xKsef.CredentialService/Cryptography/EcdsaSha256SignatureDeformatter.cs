using System.Security.Cryptography;

namespace xKsef.CredentialService.Cryptography;

public sealed class EcdsaSha256SignatureDeformatter : AsymmetricSignatureDeformatter
{
    private ECDsa? _key;

    public override void SetKey(AsymmetricAlgorithm key)
    {
        _key = key as ECDsa ?? throw new ArgumentException("Wymagany jest klucz ECDSA.", nameof(key));
    }

    public override void SetHashAlgorithm(string strName)
    {
        if (strName != "SHA256" && strName != "SHA-256" && strName != typeof(SHA256).FullName) throw new CryptographicException("Obsługiwany jest wyłącznie algorytm SHA-256.");
    }

    public override bool VerifySignature(byte[] rgbHash, byte[] rgbSignature)
    {
        if (_key == null) throw new CryptographicException("Nie ustawiono klucza ECDSA.");
        if (rgbHash == null) throw new ArgumentNullException(nameof(rgbHash));
        if (rgbSignature == null) throw new ArgumentNullException(nameof(rgbSignature));

        return _key.VerifyHash(rgbHash, rgbSignature, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
    }
}