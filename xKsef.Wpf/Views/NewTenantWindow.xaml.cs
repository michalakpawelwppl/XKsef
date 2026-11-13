using System.Windows;
using xKsef.Wpf.ViewModels;

namespace xKsef.Wpf.Views;

public partial class NewTenantWindow : Window
{
    public NewTenantWindow(NewTenantViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
