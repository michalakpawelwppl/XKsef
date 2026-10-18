using Microsoft.Extensions.DependencyInjection;
using System.Windows;
using xKsef.Wpf.Views;

namespace xKsef.Wpf.Services;

public sealed class WindowNavigationService : IWindowNavigationService
{
    private readonly IServiceProvider _serviceProvider;

    public WindowNavigationService(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    public bool? ShowLoginWindow()
    {
        var loginWindow = _serviceProvider
            .GetRequiredService<LoginWindow>();

        loginWindow.Owner = System.Windows.Application.Current.MainWindow;

        return loginWindow.ShowDialog();
    }
}