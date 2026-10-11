namespace xKsef.Infrastructure.Ksef.Configuration;

public class KsefOptions
{
    public const string SectionName = "Ksef";

    public string Environment { get; set; } = "TEST";

    public string BaseUrl { get; set; } =
        "https://api-test.ksef.mf.gov.pl";

    public string Nip { get; set; } = string.Empty;

    public string CertificatePath { get; set; } = string.Empty;
    public string CertificatePassword { get; set; } = string.Empty;
}