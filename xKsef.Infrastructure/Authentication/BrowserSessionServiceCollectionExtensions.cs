using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace xKsef.Infrastructure.Authentication;

public static class BrowserSessionServiceCollectionExtensions
{
    // Store registration only; API configures cookie authentication and the cleanup schedule.
    public static IServiceCollection AddBrowserSessionStore(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<BrowserSessionOptions>().Bind(configuration.GetSection(BrowserSessionOptions.SectionName))
            .Validate(options => options.IsValid(), "Browser session lifetimes must be positive; idle <= absolute.").ValidateOnStart();
        services.AddDataProtection();
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<SqlBrowserTicketStore>();
        services.TryAddSingleton<ITicketStore>(provider => provider.GetRequiredService<SqlBrowserTicketStore>());
        return services;
    }
}