using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using xKsef.Application.Gus;
using xKsef.Infrastructure.Gus;

namespace xKsef.Tests.Infrastructure;

public sealed class GusBirOptionsTests
{
    [Fact]
    public void DefaultConfigurationUsesOfficialTestEndpointAndPublicTestKey()
    {
        var options = new GusBirOptions();
        Assert.True(new GusBirOptionsValidator().Validate(null, options).Succeeded);
        Assert.Equal(GusBirOptions.TestServiceUrl, options.Endpoint.AbsoluteUri);
        Assert.Equal(GusBirOptions.PublicTestApiKey, options.EffectiveApiKey);
    }

    [Theory]
    [InlineData("environment")]
    [InlineData("http")]
    [InlineData("wrong-host")]
    [InlineData("wrong-path")]
    [InlineData("query")]
    [InlineData("credentials")]
    [InlineData("production-endpoint-in-test")]
    [InlineData("missing-production-key")]
    [InlineData("test-key-in-production")]
    [InlineData("short-key")]
    [InlineData("non-ascii-key")]
    [InlineData("empty-key")]
    [InlineData("zero-timeout")]
    [InlineData("long-timeout")]
    public void InvalidConfigurationIsRejectedWithoutExposingKey(string invalid)
    {
        var options = new GusBirOptions();
        switch (invalid)
        {
            case "environment": options.Environment = (GusBirEnvironment)99; break;
            case "http": options.ServiceUrl = GusBirOptions.TestServiceUrl.Replace("https:", "http:"); break;
            case "wrong-host": options.ServiceUrl = "https://example.invalid/wsBIR/UslugaBIRzewnPubl.svc"; break;
            case "wrong-path": options.ServiceUrl = "https://wyszukiwarkaregontest.stat.gov.pl/other"; break;
            case "query": options.ServiceUrl = GusBirOptions.TestServiceUrl + "?key=hidden"; break;
            case "credentials": options.ServiceUrl = GusBirOptions.TestServiceUrl.Replace("https://", "https://user:secret@"); break;
            case "production-endpoint-in-test": options.ServiceUrl = GusBirOptions.ProductionServiceUrl; break;
            case "missing-production-key": options.Environment = GusBirEnvironment.Production; break;
            case "test-key-in-production": options.Environment = GusBirEnvironment.Production; options.ApiKey = GusBirOptions.PublicTestApiKey; break;
            case "short-key": options.ApiKey = "sensitive-invalid-key"[..10]; break;
            case "non-ascii-key": options.ApiKey = new string('ą', 20); break;
            case "empty-key": options.ApiKey = ""; break;
            case "zero-timeout": options.TimeoutSeconds = 0; break;
            case "long-timeout": options.TimeoutSeconds = 301; break;
        }
        var result = new GusBirOptionsValidator().Validate(null, options);
        Assert.True(result.Failed);
        if (!string.IsNullOrEmpty(options.ApiKey))
            Assert.DoesNotContain(options.ApiKey, string.Join(";", result.Failures));
    }

    [Fact]
    public async Task RegistrationBindsConfigurationAndStartsWithoutCallingGus()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["GusBir:Environment"] = "Production",
            ["GusBir:ApiKey"] = "unitTestOnlyKey12345",
            ["GusBir:TimeoutSeconds"] = "17"
        }).Build();
        using var host = new HostBuilder().ConfigureServices(services => services.AddGusBirInfrastructure(configuration)).Build();
        await host.StartAsync();
        var options = host.Services.GetRequiredService<IOptions<GusBirOptions>>().Value;
        Assert.Equal(GusBirEnvironment.Production, options.Environment);
        Assert.Equal(GusBirOptions.ProductionServiceUrl, options.Endpoint.AbsoluteUri);
        Assert.Equal(17, options.TimeoutSeconds);
        Assert.IsType<GusBirClient>(host.Services.GetRequiredService<IGusBirClient>());
        using var http = host.Services.GetRequiredService<IHttpClientFactory>().CreateClient(GusBirServiceCollectionExtensions.HttpClientName);
        Assert.Equal(Timeout.InfiniteTimeSpan, http.Timeout);
        await host.StopAsync();
    }

    [Fact]
    public async Task MissingProductionKeyFailsAtStartupBeforeAnyNetworkCall()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["GusBir:Environment"] = "Production" }).Build();
        using var host = new HostBuilder().ConfigureServices(services => services.AddGusBirInfrastructure(configuration)).Build();
        await Assert.ThrowsAsync<OptionsValidationException>(() => host.StartAsync());
    }
}
