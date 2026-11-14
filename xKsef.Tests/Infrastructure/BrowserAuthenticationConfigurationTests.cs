using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.DataProtection.Repositories;
using Microsoft.AspNetCore.DataProtection.XmlEncryption;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using xKsef.Api.Authentication;
using xKsef.Infrastructure.Authentication;

namespace xKsef.Tests.Infrastructure;

public sealed class BrowserAuthenticationConfigurationTests
{
    [Fact]
    public async Task ThreeSchemesUseSqlTicketsAndStrictHostCookies()
    {
        using var services = Services(Configuration()).BuildServiceProvider();
        var schemes = await services.GetRequiredService<IAuthenticationSchemeProvider>().GetAllSchemesAsync();
        Assert.Equal(new[] { "ApiAuthentication", "Bearer", "BrowserCookie" }, schemes.Select(scheme => scheme.Name).Order());
        var cookie = services.GetRequiredService<IOptionsMonitor<CookieAuthenticationOptions>>().Get("BrowserCookie");
        Assert.IsType<BrowserTicketStore>(cookie.SessionStore); Assert.Equal(BrowserAuthentication.CookieName, cookie.Cookie.Name);
        Assert.True(cookie.Cookie.HttpOnly); Assert.Equal(CookieSecurePolicy.Always, cookie.Cookie.SecurePolicy);
        Assert.Equal(SameSiteMode.Lax, cookie.Cookie.SameSite); Assert.Equal("/", cookie.Cookie.Path); Assert.Null(cookie.Cookie.Domain);
        Assert.Equal(TimeSpan.FromMinutes(30), cookie.ExpireTimeSpan); Assert.True(cookie.SlidingExpiration);
        var csrf = services.GetRequiredService<IOptions<AntiforgeryOptions>>().Value;
        Assert.Equal("__Host-xksef-csrf", csrf.Cookie.Name); Assert.Equal(BrowserAuthentication.CsrfHeader, csrf.HeaderName);
        Assert.True(csrf.Cookie.HttpOnly); Assert.Equal(CookieSecurePolicy.Always, csrf.Cookie.SecurePolicy); Assert.Null(csrf.Cookie.Domain);
        var keys = services.GetRequiredService<IOptions<KeyManagementOptions>>().Value;
        Assert.IsType<FileSystemXmlRepository>(keys.XmlRepository); Assert.IsType<DpapiXmlEncryptor>(keys.XmlEncryptor);
    }

    [Theory]
    [InlineData("relative/keys")]
    [InlineData(".")]
    public void RelativeKeyRingPathsAreRejected(string path)
        => Assert.Throws<InvalidOperationException>(() => Services(Configuration(path)));

    [Fact]
    public void PublicationAndRepositoryDirectoriesAreRejectedForKeys()
    {
        foreach (string path in new[] { AppContext.BaseDirectory, Path.Combine(AppContext.BaseDirectory, "keys"), Directory.GetCurrentDirectory() })
            Assert.Throws<InvalidOperationException>(() => Services(Configuration(path)));
    }

    [Fact]
    public void UnknownKeyProtectionModeCannotSilentlyStorePlaintextKeys()
        => Assert.Throws<InvalidOperationException>(() => Services(Configuration(mode: "None")));

    [Fact]
    public void SharedCertificateProtectionRequiresAnExplicitCertificate()
        => Assert.Throws<InvalidOperationException>(() => Services(Configuration(mode: "Certificate")));

    [Fact]
    public void InvalidSessionCleanupOptionsFailValidation()
    {
        var config = Configuration(extra: new Dictionary<string, string?> { ["BrowserSessions:Cleanup:BatchSize"] = "0" });
        using var services = Services(config).BuildServiceProvider();
        Assert.Throws<OptionsValidationException>(() => services.GetRequiredService<IOptions<BrowserSessionCleanupOptions>>().Value);
    }

    [Fact]
    public void EmptyApplicationDiscriminatorIsRejected()
        => Assert.Throws<InvalidOperationException>(() => Services(Configuration(extra: new Dictionary<string, string?> { ["DataProtection:ApplicationName"] = " " })));

    private static ServiceCollection Services(IConfiguration configuration)
    {
        var services = new ServiceCollection(); services.AddLogging();
        services.AddApiAuthentication(configuration, new TestEnvironment()); return services;
    }
    private static IConfiguration Configuration(string? path = null, string mode = "DpapiCurrentUser", Dictionary<string, string?>? extra = null)
    {
        var values = new Dictionary<string, string?>
        {
            ["Jwt:Issuer"] = "test", ["Jwt:Audience"] = "test", ["Jwt:SigningKey"] = Convert.ToBase64String(new byte[32]),
            ["DataProtection:KeyRingPath"] = path ?? Path.Combine(Path.GetTempPath(), "xksef-auth-configuration-tests"), ["DataProtection:ProtectionMode"] = mode
        };
        if (extra is not null) foreach (var pair in extra) values[pair.Key] = pair.Value;
        return new ConfigurationBuilder().AddInMemoryCollection(values).Build();
    }
    private sealed class TestEnvironment : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Testing";
        public string ApplicationName { get; set; } = "Tests";
        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
