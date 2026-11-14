using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using xKsef.Api.Hosting;

namespace xKsef.Tests.Infrastructure;

public sealed class BrowserFrontendHostingTests : IAsyncLifetime
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "xksef-web-test-" + Guid.NewGuid().ToString("N"));
    private WebApplication app = null!;
    private HttpClient client = null!;
    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(root, "api"));
        await File.WriteAllTextAsync(Path.Combine(root, "index.html"), "<html>frontend</html>");
        await File.WriteAllTextAsync(Path.Combine(root, "app.js"), "console.log('static');");
        await File.WriteAllTextAsync(Path.Combine(root, "api", "protected"), "must not leak");
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["BrowserFrontend:Path"] = root });
        app = builder.Build();
        app.UseBrowserFrontend();
        app.MapGet("/api/probe", () => Results.Json(new { source = "api" }));
        app.MapGet("/api/protected", () => Results.Unauthorized());
        await app.StartAsync(); client = app.GetTestClient();
    }
    public async Task DisposeAsync()
    {
        client.Dispose(); await app.DisposeAsync(); Directory.Delete(root, true);
    }
    [Theory]
    [InlineData("/")]
    [InlineData("/login")]
    [InlineData("/app/invoices")]
    public async Task ClientRoutesReturnUncachedFrontend(string path)
    {
        using var response = await client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/html", response.Content.Headers.ContentType!.MediaType);
        Assert.True(response.Headers.CacheControl!.NoStore);
        Assert.Contains("frontend", await response.Content.ReadAsStringAsync());
    }
    [Fact]
    public async Task AssetsAreServedButMissingAssetsDoNotReturnHtml()
    {
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/app.js")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/missing.js")).StatusCode);
    }
    [Fact]
    public async Task ApiRoutingAndAuthorizationCannotBeShadowedByFilesOrFallback()
    {
        Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/probe")).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/protected")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/api/unknown")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.GetAsync("/API/unknown")).StatusCode);
    }
    [Fact]
    public async Task FallbackNeverHandlesMutations()
    {
        using var response = await client.PostAsync("/login", new StringContent("{}"));
        Assert.False(response.IsSuccessStatusCode);
    }
    [Fact]
    public async Task HostingIsOptIn()
    {
        var builder = WebApplication.CreateBuilder(); builder.WebHost.UseTestServer();
        await using var disabled = builder.Build(); disabled.UseBrowserFrontend(); await disabled.StartAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await disabled.GetTestClient().GetAsync("/login")).StatusCode);
    }
    [Theory]
    [InlineData("relative/dist")]
    [InlineData("C:\\xksef-does-not-exist\\dist")]
    public void InvalidPathsFailExplicitly(string path)
    {
        var builder = WebApplication.CreateBuilder();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["BrowserFrontend:Path"] = path });
        using var invalid = builder.Build();
        Assert.Throws<InvalidOperationException>(() => invalid.UseBrowserFrontend());
    }
}
