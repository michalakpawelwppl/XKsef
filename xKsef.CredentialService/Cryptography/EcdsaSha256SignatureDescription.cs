using System.Security.Cryptography;

namespace xKsef.CredentialService.Cryptography;

public sealed class EcdsaSha256SignatureDescription : SignatureDescription
{
    public EcdsaSha256SignatureDescription()
    {
        KeyAlgorithm = typeof(ECDsa).FullName;
        DigestAlgorithm = typeof(SHA256).FullName;
        FormatterAlgorithm = typeof(EcdsaSha256SignatureFormatter).FullName;
        DeformatterAlgorithm = typeof(EcdsaSha256SignatureDeformatter).FullName;
    }

    public override HashAlgorithm CreateDigest()
    {
        return SHA256.Create();
    }

    public override AsymmetricSignatureFormatter CreateFormatter(AsymmetricAlgorithm key)
    {
        var formatter = new EcdsaSha256SignatureFormatter();
        formatter.SetKey(key);
        return formatter;
    }

    public override AsymmetricSignatureDeformatter CreateDeformatter(AsymmetricAlgorithm key)
    {
        var deformatter = new EcdsaSha256SignatureDeformatter();
        deformatter.SetKey(key);
        return deformatter;
    }
}