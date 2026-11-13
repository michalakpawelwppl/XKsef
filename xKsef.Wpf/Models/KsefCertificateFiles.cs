namespace xKsef.Wpf.Models;

public sealed class KsefCertificateFiles
{
    public string CertificatePath { get; set; } = string.Empty;

    public string PrivateKeyPath { get; set; } = string.Empty;

    public string? Password { get; set; }
}