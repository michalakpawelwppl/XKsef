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
                System.Windows.MessageBox.Show(window, "Konto zostało utworzone. Zaloguj się, aby kontynuować.", "Nowy użytkownik", System.Windows.MessageBoxButton.OK, System.Windows.MessageBoxImage.Information);
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

    public void ShowTenantUsersWindow(Action<string?> currentAccessChanged)
    {
        var window = _serviceProvider.GetRequiredService<TenantUsersWindow>();
        var viewModel = (TenantUsersViewModel)window.DataContext;
        window.Owner = System.Windows.Application.Current.MainWindow;
        void CloseWindow() => window.DialogResult = false;
        async void Loaded(object sender, System.Windows.RoutedEventArgs args)
        {
            if (!viewModel.IsValid) { CloseWindow(); return; }
            await viewModel.RefreshCommand.ExecuteAsync(null);
        }
        viewModel.CloseRequested += CloseWindow;
        viewModel.CurrentAccessChanged += currentAccessChanged;
        window.Loaded += Loaded;
        try { window.ShowDialog(); }
        finally { window.Loaded -= Loaded; viewModel.CloseRequested -= CloseWindow; viewModel.CurrentAccessChanged -= currentAccessChanged; viewModel.Dispose(); }
    }
    public bool? ShowLoginWindow()
    {
        var loginWindow = _serviceProvider.GetRequiredService<LoginWindow>();
        loginWindow.Owner = System.Windows.Application.Current.MainWindow;

        return loginWindow.ShowDialog();
    }
}