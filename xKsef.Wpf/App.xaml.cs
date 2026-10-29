using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using xKsef.Wpf.Authentication;
using xKsef.Wpf.Services;
using xKsef.Wpf.ViewModels;
using xKsef.Wpf.Views;

namespace xKsef.Wpf;

public partial class App : System.Windows.Application
{
    private readonly IHost _host;


    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .ConfigureServices((context, services) =>
            {
                var apiBaseUrl = context.Configuration["Api:BaseUrl"]
                    ?? throw new InvalidOperationException(
                        "Brak konfiguracji Api:BaseUrl.");

                services.AddHttpClient<IInvoiceApiClient, InvoiceApiClient>(
                    client =>
                    {
                        client.BaseAddress = new Uri(apiBaseUrl);
                        client.Timeout = TimeSpan.FromMinutes(15);
                    })
                    .AddHttpMessageHandler<BearerTokenHandler>();

                services.AddHttpClient<IAuthenticationApiClient, AuthenticationApiClient>(
                    client =>
                    {
                        client.BaseAddress = new Uri(apiBaseUrl);
                    });


                services.AddTransient<MainViewModel>();
                services.AddTransient<MainWindow>();
                services.AddTransient<LoginViewModel>();
                services.AddTransient<LoginWindow>();
                services.AddSingleton<ITokenStore, TokenStore>();
                services.AddTransient<BearerTokenHandler>();
                services.AddSingleton<IWindowNavigationService, WindowNavigationService>();
            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        ShutdownMode = ShutdownMode.OnMainWindowClose;

        await _host.StartAsync();

        var window = _host.Services
            .GetRequiredService<MainWindow>();

        MainWindow = window;
        window.Show();
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        await _host.StopAsync();
        _host.Dispose();

        base.OnExit(e);
    }
}