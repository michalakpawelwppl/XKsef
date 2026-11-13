using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using xKsef.Wpf.Models;
using xKsef.Wpf.ViewModels;
using xKsef.Wpf.Views;

namespace xKsef.Wpf.Services.Certificates;

public sealed class CertificateSelectionWindowService : ICertificateSelectionWindowService
{
    private readonly IServiceProvider _serviceProvider;

    public CertificateSelectionWindowService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public KsefCertificateFiles? SelectCertificate()
    {
        var window = _serviceProvider.GetRequiredService<CertificateSelectionWindow>();
        var viewModel = (CertificateSelectionViewModel)window.DataContext;

        void CloseWindow(bool? result)
        {
            window.DialogResult = result;
        }

        viewModel.CloseRequested += CloseWindow;

        try
        {
            window.Owner = System.Windows.Application.Current.Windows.OfType<Window>().FirstOrDefault(w => w.IsActive && w != window);

            if (window.ShowDialog() != true)
                return null;

            return new KsefCertificateFiles
            {
                CertificatePath = viewModel.CertificatePath,
                PrivateKeyPath = viewModel.PrivateKeyPath,
                Password = viewModel.Password
            };
        }
        finally
        {
            viewModel.CloseRequested -= CloseWindow;
        }
    }
}