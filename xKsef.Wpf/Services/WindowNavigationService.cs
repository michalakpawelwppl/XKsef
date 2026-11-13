using Microsoft.Extensions.DependencyInjection;
using xKsef.Wpf.Views;
using xKsef.Wpf.ViewModels;
using xKsef.Wpf.Models;

namespace xKsef.Wpf.Services;

public sealed class WindowNavigationService : IWindowNavigationService
{
    private readonly IServiceProvider _serviceProvider;

    public WindowNavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public TenantCreationDialogResult? ShowNewTenantWindow()
    {
        var window = _serviceProvider.GetRequiredService<NewTenantWindow>();
        var viewModel = (NewTenantViewModel)window.DataContext;
        window.Owner = System.Windows.Application.Current.MainWindow;

        void CloseWindow(bool? result)
        {
            if (result == true)
                System.Windows.MessageBox.Show(window, "Tenant został utworzony.", "Nowy tenant", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            window.DialogResult = result;
        }

        void OnClosed(object? sender, EventArgs args) => viewModel.Dispose();
        viewModel.CloseRequested += CloseWindow;
        window.Closed += OnClosed;
        try
        {
            return window.ShowDialog() == true ? viewModel.CreatedTenant : null;
        }
        finally
        {
            viewModel.CloseRequested -= CloseWindow;
            window.Closed -= OnClosed;
            viewModel.Dispose();
        }
    }

    public bool? ShowNewUserWindow()
    {
        var window = _serviceProvider.GetRequiredService<NewUserWindow>();
        var viewModel = (NewUserViewModel)window.DataContext;
        window.Owner = System.Windows.Application.Current.MainWindow;

        void ClearForm()
        {
            viewModel.Dispose();
            window.PasswordInput.Clear();
            window.ConfirmPasswordInput.Clear();
        }

        void CloseWindow(bool? result)
        {
            if (result == true)
                System.Windows.MessageBox.Show(window, "Użytkownik został utworzony.", "Nowy użytkownik", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
            window.DialogResult = result;
        }

        void OnClosed(object? sender, EventArgs args) => ClearForm();
        viewModel.CloseRequested += CloseWindow;
        window.Closed += OnClosed;
        try
        {
            return window.ShowDialog();
        }
        finally
        {
            viewModel.CloseRequested -= CloseWindow;
            window.Closed -= OnClosed;
            ClearForm();
        }
    }

    public bool? ShowLoginWindow()
    {
        var loginWindow = _serviceProvider.GetRequiredService<LoginWindow>();
        loginWindow.Owner = System.Windows.Application.Current.MainWindow;

        return loginWindow.ShowDialog();
    }
}