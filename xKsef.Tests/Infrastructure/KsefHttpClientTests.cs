using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Infrastructure;
using Xunit;

namespace xKsef.Tests.Infrastructure;

public class KsefHttpClientTests
{
    [Fact]
    public void KsefPackageDownload_HttpClient_HasCorrectTimeout()
    {
        // Arrange
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ksef:Environment"] = "TEST"
            })
            .Build();

        var services = new ServiceCollection();

        // Rzeczywista konfiguracja aplikacji
        services.AddKsefInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();

        var factory =
            provider.GetRequiredService<IHttpClientFactory>();

        // Act
        using var client =
            factory.CreateClient("KsefPackageDownload");

        // Assert
        Assert.Equal(TimeSpan.FromMinutes(10), client.Timeout);
    }
}