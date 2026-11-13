using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Win32;
using System.IO;

namespace xKsef.Wpf.ViewModels;

public partial class CertificateSelectionViewModel : ObservableObject
{
    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string certificatePath = string.Empty;

    [ObservableProperty]
    private string privateKeyPath = string.Empty;

    public event Action<bool?>? CloseRequested;

    [RelayCommand]
    private void BrowseCertificate()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Wybierz certyfikat KSeF",
            Filter = "Certyfikaty (*.crt;*.pem)|*.crt;*.pem|Wszystkie pliki (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
            CertificatePath = dialog.FileName;
    }

    [RelayCommand]
    private void BrowsePrivateKey()
    {
        var dialog = new OpenFileDialog
        {
            Title = "Wybierz klucz prywatny KSeF",
            Filter = "Klucze prywatne (*.key;*.pem)|*.key;*.pem|Wszystkie pliki (*.*)|*.*",
            CheckFileExists = true
        };

        if (dialog.ShowDialog() == true)
            PrivateKeyPath = dialog.FileName;
    }

    [RelayCommand(CanExecute = nameof(CanConfirm))]
    private void Confirm()
    {
        CloseRequested?.Invoke(true);
    }

    private bool CanConfirm() => File.Exists(CertificatePath) && File.Exists(PrivateKeyPath);

    [RelayCommand]
    private void Cancel()
    {
        CloseRequested?.Invoke(false);
    }

    partial void OnCertificatePathChanged(string value)
    {
        ConfirmCommand.NotifyCanExecuteChanged();
    }

    partial void OnPrivateKeyPathChanged(string value)
    {
        ConfirmCommand.NotifyCanExecuteChanged();
    }
}