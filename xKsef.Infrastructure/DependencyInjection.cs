using KSeF.Client.Api.Services;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.DI;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Application.Invoices;
using xKsef.Infrastructure.Ksef;
using xKsef.Infrastructure.Ksef.Authentication;
using xKsef.Infrastructure.Ksef.Configuration;
using xKsef.Infrastructure.Ksef.Invoices;

namespace xKsef.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddKsefInfrastructure(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        var ksefOptions = configuration
            .GetSection(KsefOptions.SectionName)
            .Get<KsefOptions>()
            ?? throw new InvalidOperationException(
                "Brak konfiguracji Ksef.");
        services.AddScoped<KsefAuthenticationService>();
        services.AddSingleton<KsefTokenStore>();
        services.AddSingleton<KsefTokenProvider>();
        services.Configure<KsefOptions>(configuration.GetSection(KsefOptions.SectionName));
        services.AddScoped<KsefInvoiceService>();
        services.AddSingleton<KsefRateLimitState>();
        services.AddScoped<ICertificateFetcher, KsefCertificateFetcher>();
        services.AddScoped<ICryptographyService, CryptographyService>();
        services.AddHttpClient("KsefPackageDownload", client =>
        {
            client.Timeout = TimeSpan.FromMinutes(10);
        });
        services.AddScoped<IInvoiceService>(
    provider => provider.GetRequiredService<KsefInvoiceService>());

        if (string.IsNullOrWhiteSpace(ksefOptions.BaseUrl))
        {
            throw new InvalidOperationException(
                "Nie skonfigurowano adresu KSeF API.");
        }

        services.AddKSeFClient(options =>
        {
            options.BaseUrl = ksefOptions.BaseUrl;
        });

        services.AddCryptographyClient();

        return services;
    }
}