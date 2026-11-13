using System.Security.Cryptography.X509Certificates;
using System.Security.Cryptography.Xml;
using System.Xml;
namespace xKsef.Infrastructure.Ksef.Authentication;

public sealed class KsefAuthenticationXmlValidator
{
    public void Validate(string signedXml, PendingKsefAuthentication pending)
    {
        if (string.IsNullOrWhiteSpace(signedXml))
            throw new InvalidOperationException("Brak podpisanego dokumentu XML.");

        if (System.Text.Encoding.UTF8.GetByteCount(signedXml) > 1024 * 1024)
            throw new InvalidOperationException("Podpisany dokument XML przekracza dopuszczalny rozmiar 1 MB.");
        
        var document = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };

        using var reader = XmlReader.Create(new StringReader(signedXml), new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        document.Load(reader);

        if (document.DocumentElement?.LocalName != "AuthTokenRequest" || document.DocumentElement.NamespaceURI != "http://ksef.mf.gov.pl/auth/token/2.0")
            throw new InvalidOperationException("Nieprawidłowy dokument uwierzytelniania KSeF.");

        var signatures = document.GetElementsByTagName("Signature", "http://www.w3.org/2000/09/xmldsig#");

        if (signatures.Count != 1 || signatures[0] is not XmlElement signatureElement)
            throw new InvalidOperationException("Dokument musi zawierać dokładnie jeden podpis XML.");

        VerifySignature(document, signatureElement);

        var challengeElements = document.DocumentElement!.GetElementsByTagName("Challenge", document.DocumentElement.NamespaceURI);

        if (challengeElements.Count != 1 || challengeElements[0]?.InnerText != pending.Challenge)
            throw new InvalidOperationException("Challenge w podpisanym XML nie odpowiada rozpoczętej operacji uwierzytelniania.");
        
        ValidateNip(document, pending);

        var subjectIdentifierTypes = document.DocumentElement!.GetElementsByTagName("SubjectIdentifierType", document.DocumentElement.NamespaceURI);

        if (subjectIdentifierTypes.Count != 1 || subjectIdentifierTypes[0]?.InnerText != "certificateSubject")
            throw new InvalidOperationException("Nieprawidłowy typ identyfikatora podmiotu.");
    }

    private static void ValidateNip(XmlDocument document, PendingKsefAuthentication pending)
    {
        var ns = new XmlNamespaceManager(document.NameTable);
        ns.AddNamespace("k", document.DocumentElement!.NamespaceURI);

        var context = document.SelectSingleNode("/k:AuthTokenRequest/k:ContextIdentifier", ns);

        var elements = context?.ChildNodes.OfType<XmlElement>().ToArray();

        if (elements?.Length != 1 || elements[0].LocalName != "Nip" || elements[0].NamespaceURI != document.DocumentElement!.NamespaceURI || elements[0].InnerText != pending.Nip)
            throw new InvalidOperationException("NIP w podpisanym XML nie odpowiada NIP-owi wybranego tenanta.");
    }

    private static X509Certificate2 GetSigningCertificate(XmlElement signatureElement)
    {
        var certificates = signatureElement.GetElementsByTagName("X509Certificate", "http://www.w3.org/2000/09/xmldsig#");

        if (certificates.Count != 1 || string.IsNullOrWhiteSpace(certificates[0]?.InnerText))
            throw new InvalidOperationException("Podpis XML nie zawiera dokładnie jednego certyfikatu.");

        return X509CertificateLoader.LoadCertificate(Convert.FromBase64String(certificates[0]!.InnerText));
    }

    private static void VerifySignature(XmlDocument document, XmlElement signatureElement)
    {
        using var certificate = GetSigningCertificate(signatureElement);

        var signedXml = new KsefSignedXml(document, signatureElement);
        signedXml.LoadXml(signatureElement);

        var signatureMethod = signedXml.SignedInfo?.SignatureMethod;

        if (signatureMethod != SignedXml.XmlDsigRSASHA256Url && signatureMethod != "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256")
            throw new InvalidOperationException("Nieobsługiwany algorytm podpisu XML.");

        ValidateSignatureReferences(signedXml);

        if (!signedXml.CheckSignature(certificate, true))
            throw new InvalidOperationException("Podpis XML jest nieprawidłowy.");
    }

    private static void ValidateSignatureReferences(SignedXml signedXml)
    {
        if (signedXml.SignedInfo?.References.Count != 2)
            throw new InvalidOperationException("Nieprawidłowa liczba referencji podpisu.");

        var references = signedXml.SignedInfo.References.Cast<Reference>().ToArray();

        if (references.Count(r => r.Uri == "") != 1)
            throw new InvalidOperationException("Podpis nie obejmuje całego dokumentu XML.");

        if (references.Count(r => r.Uri == "#SignedProperties" && r.Type == "http://uri.etsi.org/01903#SignedProperties") != 1)
            throw new InvalidOperationException("Brak prawidłowej referencji SignedProperties.");

        var documentReference = references.Single(r => r.Uri == "");
        var propertiesReference = references.Single(r => r.Uri == "#SignedProperties");

        if (references.Any(r => r.DigestMethod != SignedXml.XmlDsigSHA256Url))
            throw new InvalidOperationException("Nieprawidłowy algorytm skrótu podpisu.");

        if (documentReference.TransformChain.Count != 2 || documentReference.TransformChain[0].Algorithm != SignedXml.XmlDsigEnvelopedSignatureTransformUrl || documentReference.TransformChain[1].Algorithm != SignedXml.XmlDsigExcC14NTransformUrl)
            throw new InvalidOperationException("Nieprawidłowe transformacje dokumentu XML.");

        if (propertiesReference.TransformChain.Count != 1 || propertiesReference.TransformChain[0].Algorithm != SignedXml.XmlDsigExcC14NTransformUrl)
            throw new InvalidOperationException("Nieprawidłowa transformacja SignedProperties.");

    }

    private sealed class KsefSignedXml : SignedXml
    {
        private readonly XmlElement _signatureElement;

        public KsefSignedXml(XmlDocument document, XmlElement signatureElement) : base(document)
        {
            _signatureElement = signatureElement;
        }

        public override XmlElement? GetIdElement(XmlDocument document, string idValue)
        {
            var element = base.GetIdElement(document, idValue);

            if (element != null)
                return element;

            var properties = _signatureElement.GetElementsByTagName("SignedProperties", "http://uri.etsi.org/01903/v1.3.2#");

            if (properties.Count == 1 && properties[0] is XmlElement signedProperties && signedProperties.GetAttribute("Id") == idValue)
                return signedProperties;

            return null;
        }
    }
}