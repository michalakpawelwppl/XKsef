using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Infrastructure;
using Xunit;

namespace xKsef.Tests.Infrastructure;

public class KsefHttpClientTests
{
    [Theory]
    [InlineData(null, true)]
    [InlineData("true", true)]
    [InlineData("false", false)]
    public void StartupWarmupCanBeExplicitlyDisabledForOfflineVerification(string? setting, bool expected)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ksef:Environment"] = "TEST",
            ["Ksef:WarmupOnStartup"] = setting
        }).Build();
        var services = new ServiceCollection();
        services.AddKsefInfrastructure(configuration);
        Assert.Equal(expected, services.Any(service => service.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService)
            && service.ImplementationType == typeof(KsefCryptographyWarmupService)));
    }
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