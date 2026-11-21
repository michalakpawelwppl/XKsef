using System.Net;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using xKsef.Application.Gus;

namespace xKsef.Infrastructure.Gus;

public static class GusBirServiceCollectionExtensions
{
    public const string HttpClientName = "GusBir";

    public static IServiceCollection AddGusBirInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddOptions<GusBirOptions>().Bind(configuration.GetSection(GusBirOptions.SectionName)).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<GusBirOptions>, GusBirOptionsValidator>());
        services.TryAddSingleton<TimeProvider>(TimeProvider.System);
        services.TryAddSingleton<IGusBirClient, GusBirClient>();
        services.AddHttpClient(HttpClientName, http => http.Timeout = Timeout.InfiniteTimeSpan)
            .ConfigurePrimaryHttpMessageHandler(() => new SocketsHttpHandler
            {
                AllowAutoRedirect = false,
                UseCookies = false,
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
            })
            .RedactLoggedHeaders(["sid"]);
        return services;
    }
}
