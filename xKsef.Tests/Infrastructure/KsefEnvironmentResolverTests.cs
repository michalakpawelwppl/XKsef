using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Ksef.Configuration;

namespace xKsef.Tests.Infrastructure;

public class KsefEnvironmentResolverTests
{
    [Theory]
    [InlineData("TEST", "https://api-test.ksef.mf.gov.pl")]
    [InlineData("PROD", "https://api.ksef-prod.mf.gov.pl")]
    [InlineData(" test ", "https://api-test.ksef.mf.gov.pl")]
    [InlineData(" prod ", "https://api.ksef-prod.mf.gov.pl")]
    public void ResolveBaseUrl_UsesSelectedEnvironment(string environment, string expectedBaseUrl)
    {
        IConfiguration configuration = CreateConfiguration(environment);

        Assert.Equal(expectedBaseUrl, KsefEnvironmentResolver.ResolveBaseUrl(configuration));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("DEMO")]
    [InlineData("Production")]
    [InlineData("Development")]
    public void ResolveBaseUrl_RejectsMissingOrUnknownEnvironment(string? environment)
    {
        IConfiguration configuration = CreateConfiguration(environment);

        var exception = Assert.Throws<InvalidOperationException>(() => KsefEnvironmentResolver.ResolveBaseUrl(configuration));

        Assert.Contains("Ksef:Environment", exception.Message);
    }

    [Theory]
    [InlineData("TEST", "https://api-test.ksef.mf.gov.pl")]
    [InlineData("PROD", "https://api.ksef-prod.mf.gov.pl")]
    [InlineData("TEST", "https://api-test.ksef.mf.gov.pl/")]
    [InlineData("PROD", " https://api.ksef-prod.mf.gov.pl/ ")]
    public void ResolveBaseUrl_AcceptsMatchingBaseUrlAndReturnsCanonicalAddress(string environment, string baseUrl)
    {
        IConfiguration configuration = CreateConfiguration(environment, baseUrl);

        Assert.Equal(KsefEnvironmentResolver.ResolveBaseUrl(CreateConfiguration(environment)), KsefEnvironmentResolver.ResolveBaseUrl(configuration));
    }

    [Theory]
    [InlineData("TEST", "https://api.ksef-prod.mf.gov.pl")]
    [InlineData("PROD", "https://api-test.ksef.mf.gov.pl")]
    [InlineData("TEST", "https://example.com")]
    [InlineData("TEST", "http://api-test.ksef.mf.gov.pl")]
    [InlineData("TEST", "https://api-test.ksef.mf.gov.pl:444")]
    [InlineData("TEST", "https://api-test.ksef.mf.gov.pl/v2")]
    [InlineData("TEST", "https://api-test.ksef.mf.gov.pl?environment=PROD")]
    [InlineData("TEST", "https://api-test.ksef.mf.gov.pl#fragment")]
    [InlineData("TEST", "https://user:sensitive@api-test.ksef.mf.gov.pl")]
    [InlineData("TEST", "not a URL")]
    [InlineData("TEST", "")]
    [InlineData("TEST", " ")]
    public void ResolveBaseUrl_RejectsInvalidOrConflictingBaseUrl(string environment, string baseUrl)
    {
        IConfiguration configuration = CreateConfiguration(environment, baseUrl);

        var exception = Assert.Throws<InvalidOperationException>(() => KsefEnvironmentResolver.ResolveBaseUrl(configuration));

        Assert.Contains("Ksef:BaseUrl", exception.Message);
        Assert.Contains("Ksef:Environment", exception.Message);
        Assert.DoesNotContain("sensitive", exception.Message);
    }

    [Fact]
    public void ResolveBaseUrl_BaseUrlAloneDoesNotReplaceExplicitEnvironment()
    {
        IConfiguration configuration = CreateConfiguration(null, "https://api-test.ksef.mf.gov.pl");

        var exception = Assert.Throws<InvalidOperationException>(() => KsefEnvironmentResolver.ResolveBaseUrl(configuration));

        Assert.Contains("Ksef:Environment", exception.Message);
    }

    [Theory]
    [InlineData("TEST", "https://api-test.ksef.mf.gov.pl")]
    [InlineData("PROD", "https://api.ksef-prod.mf.gov.pl")]
    public void AddKsefInfrastructure_ExposesResolvedAddressInOptions(string environment, string expectedBaseUrl)
    {
        IConfiguration configuration = CreateConfiguration(environment);
        var services = new ServiceCollection();

        services.AddKsefInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        KsefOptions options = provider.GetRequiredService<IOptions<KsefOptions>>().Value;
        Assert.Equal(environment, options.Environment);
        Assert.Equal(expectedBaseUrl, options.BaseUrl);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("DEMO", null)]
    [InlineData("TEST", "https://api.ksef-prod.mf.gov.pl")]
    [InlineData("PROD", "https://api-test.ksef.mf.gov.pl")]
    public void AddKsefInfrastructure_RejectsInvalidConfigurationDuringRegistration(string? environment, string? baseUrl)
    {
        IConfiguration configuration = CreateConfiguration(environment, baseUrl);
        var services = new ServiceCollection();

        Assert.Throws<InvalidOperationException>(() => services.AddKsefInfrastructure(configuration));
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void ResolveBaseUrl_LaterConfigurationOverridesTestIndependentlyOfHostEnvironment(string hostEnvironment)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ksef:Environment"] = "TEST",
                ["ASPNETCORE_ENVIRONMENT"] = hostEnvironment
            })
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ksef:Environment"] = "PROD" })
            .Build();

        Assert.Equal("https://api.ksef-prod.mf.gov.pl", KsefEnvironmentResolver.ResolveBaseUrl(configuration));
    }

    [Fact]
    public void ResolveBaseUrl_OverrideDoesNotHideConflictingBaseUrlFromEarlierSource()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Ksef:Environment"] = "TEST",
                ["Ksef:BaseUrl"] = "https://api-test.ksef.mf.gov.pl"
            })
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Ksef:Environment"] = "PROD" })
            .Build();

        Assert.Throws<InvalidOperationException>(() => KsefEnvironmentResolver.ResolveBaseUrl(configuration));
    }

    private static IConfiguration CreateConfiguration(string? environment, string? baseUrl = null)
    {
        return new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Ksef:Environment"] = environment,
            ["Ksef:BaseUrl"] = baseUrl
        }).Build();
    }
}
