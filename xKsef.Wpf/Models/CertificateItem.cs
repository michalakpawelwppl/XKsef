using System.Security.Cryptography.X509Certificates;

namespace xKsef.Wpf.Models;

public sealed class CertificateItem
{
    public required X509Certificate2 Certificate { get; init; }

    public string Subject => Certificate.GetNameInfo(X509NameType.SimpleName, false);

    public string Issuer => Certificate.GetNameInfo(X509NameType.SimpleName, true);

    public string Thumbprint => Certificate.Thumbprint;

    public DateTime ValidTo => Certificate.NotAfter;

    public string DisplayName => $"{Subject} (ważny do {ValidTo:dd.MM.yyyy})";
}