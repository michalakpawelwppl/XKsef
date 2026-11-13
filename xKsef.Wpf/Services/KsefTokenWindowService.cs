using System.ComponentModel;
using System.Windows;
using xKsef.Wpf.ViewModels;
using xKsef.Wpf.Views;

namespace xKsef.Wpf.Services;

public sealed class KsefTokenWindowService : IKsefTokenWindowService
{
    public string? ShowTokenDialog()
    {
        var viewModel = new KsefTokenViewModel();
        var window = new KsefTokenWindow { DataContext = viewModel, Owner = System.Windows.Application.Current.MainWindow };

        bool? result = null;

        PropertyChangedEventHandler handler = (sender, args) =>
        {
            if (args.PropertyName != nameof(KsefTokenViewModel.DialogResult)) return;

            result = viewModel.DialogResult;
            window.Close();
        };

        viewModel.PropertyChanged += handler;

        try
        {
            window.ShowDialog();
            return result == true ? viewModel.Token : null;
        }
        finally
        {
            viewModel.PropertyChanged -= handler;
            viewModel.Token = string.Empty;
        }
    }
}