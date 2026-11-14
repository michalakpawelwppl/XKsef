using System.Windows;
using xKsef.Wpf.ViewModels;

namespace xKsef.Wpf.Views;

public partial class TenantUsersWindow : Window
{
    public TenantUsersWindow(TenantUsersViewModel viewModel) { InitializeComponent(); DataContext = viewModel; }
}