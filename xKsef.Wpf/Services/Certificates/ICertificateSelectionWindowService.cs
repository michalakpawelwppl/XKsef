using xKsef.Wpf.Models;

namespace xKsef.Wpf.Services.Certificates;

public interface ICertificateSelectionWindowService
{
    KsefCertificateFiles? SelectCertificate();
}