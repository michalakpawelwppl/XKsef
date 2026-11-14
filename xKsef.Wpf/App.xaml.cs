using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Windows;
using xKsef.Wpf.Authentication;
using xKsef.Wpf.Services;
using xKsef.Wpf.Services.Certificates;
using xKsef.Wpf.ViewModels;
using xKsef.Wpf.Views;

namespace xKsef.Wpf;

public partial class App : System.Windows.Application
{
    private readonly IHost _host;


    public App()
    {
        _host = Host.CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices((context, services) =>
            {
                var apiBaseUrl = context.Configuration["Api:BaseUrl"] ?? throw new InvalidOperationException("Brak konfiguracji Api:BaseUrl.");
                services.AddHttpClient<IInvoiceApiClient, InvoiceApiClient>(client =>
                {
                    client.BaseAddress = new Uri(apiBaseUrl);
                    client.Timeout = TimeSpan.FromMinutes(15);
                })
                .AddHttpMessageHandler<BearerTokenHandler>()
                .AddHttpMessageHandler<TenantHeaderHandler>();

                services.AddHttpClient<ITenantApiClient, TenantApiClient>(client =>
                {
                    client.BaseAddress = new Uri(apiBaseUrl);
                })
                .AddHttpMessageHandler<BearerTokenHandler>();

                services.AddHttpClient<IAuthenticationApiClient, AuthenticationApiClient>(client =>
                {
                    client.BaseAddress = new Uri(apiBaseUrl);
                })
                .AddHttpMessageHandler<BearerTokenHandler>()
                .AddHttpMessageHandler<TenantHeaderHandler>();

                services.AddHttpClient<IKsefCredentialsApiClient, KsefCredentialsApiClient>(client =>
                {
                    client.BaseAddress = new Uri(apiBaseUrl);
                })
                .AddHttpMessageHandler<BearerTokenHandler>()
                .AddHttpMessageHandler<TenantHeaderHandler>();

                services.AddHttpClient<ITenantUsersApiClient, TenantUsersApiClient>(client => client.BaseAddress = new Uri(apiBaseUrl))
                    .AddHttpMessageHandler<BearerTokenHandler>().AddHttpMessageHandler<TenantHeaderHandler>();
                services.AddTransient<TenantUsersViewModel>();
                services.AddTransient<TenantUsersWindow>();
                services.AddTransient<MainViewModel>();
                services.AddTransient<MainWindow>();
                services.AddTransient<NewTenantViewModel>();
                services.AddTransient<NewTenantWindow>();
                services.AddTransient<NewUserViewModel>();
                services.AddTransient<NewUserWindow>();
                services.AddTransient<LoginViewModel>();
                services.AddTransient<LoginWindow>();
                services.AddSingleton<ITokenStore, TokenStore>();
                services.AddTransient<BearerTokenHandler>();
                services.AddTransient<TenantHeaderHandler>();
                services.AddSingleton<TenantContext>();
                services.AddSingleton<IWindowNavigationService, WindowNavigationService>();
                services.AddTransient<CertificateSelectionViewModel>();
                services.AddTransient<CertificateSelectionWindow>();
                services.AddSingleton<ICertificateSelectionWindowService, CertificateSelectionWindowService>();
                services.AddSingleton<IKsefTokenWindowService, KsefTokenWindowService>();

            })
            .Build();
    }

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        ShutdownMode = ShutdownMode.OnMainWindowClose;
        await _host.StartAsync();
        var window = _host.Services.GetRequiredService<MainWindow>();
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