using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Certificates;

public class KsefCertificateFetcher : ICertificateFetcher
{
    private readonly ICryptographyClient _cryptographyClient;

    public KsefCertificateFetcher(ICryptographyClient cryptographyClient)
    {
        _cryptographyClient = cryptographyClient;
    }

    public Task<ICollection<PemCertificateInfo>> GetCertificatesAsync(
        CancellationToken cancellationToken)
    {
        return _cryptographyClient.GetPublicCertificatesAsync(
            cancellationToken);
    }
}