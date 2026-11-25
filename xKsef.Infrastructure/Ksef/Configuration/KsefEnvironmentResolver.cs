using Microsoft.Extensions.Configuration;

namespace xKsef.Infrastructure.Ksef.Configuration;

internal static class KsefEnvironmentResolver
{
    public static string GetEnvironment(string resolvedBaseUrl) => resolvedBaseUrl switch
    {
        "https://api-test.ksef.mf.gov.pl" => "TEST",
        "https://api.ksef-prod.mf.gov.pl" => "PROD",
        _ => throw new InvalidOperationException("Nieprawidłowy zwalidowany adres KSeF.")
    };

    public static string ResolveBaseUrl(IConfiguration configuration)
    {
        string baseUrl = configuration["Ksef:Environment"]?.Trim().ToUpperInvariant() switch
        {
            "TEST" => "https://api-test.ksef.mf.gov.pl",
            "PROD" => "https://api.ksef-prod.mf.gov.pl",
            _ => throw new InvalidOperationException("Nieprawidłowa konfiguracja Ksef:Environment. Wymagana wartość TEST lub PROD.")
        };

        string? configuredBaseUrl = configuration["Ksef:BaseUrl"];
        if (configuredBaseUrl == null) return baseUrl;

        if (!Uri.TryCreate(configuredBaseUrl.Trim(), UriKind.Absolute, out Uri? uri)
            || uri.Scheme != Uri.UriSchemeHttps || !uri.IsDefaultPort
            || !string.Equals(uri.Host, new Uri(baseUrl).Host, StringComparison.OrdinalIgnoreCase)
            || uri.UserInfo.Length != 0 || uri.AbsolutePath != "/" || uri.Query.Length != 0 || uri.Fragment.Length != 0)
        {
            throw new InvalidOperationException("Nieprawidłowa konfiguracja Ksef:BaseUrl. Adres musi być zgodny z Ksef:Environment i wskazywać bazowy adres HTTPS wybranego środowiska KSeF.");
        }

        return baseUrl;
    }
}
