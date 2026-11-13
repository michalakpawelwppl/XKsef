using System.Windows;
using xKsef.Wpf.ViewModels;

namespace xKsef.Wpf.Views;

public partial class CertificateSelectionWindow : Window
{
    public CertificateSelectionWindow(CertificateSelectionViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}