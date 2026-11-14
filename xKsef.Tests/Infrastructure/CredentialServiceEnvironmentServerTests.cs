using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using xKsef.Application.Exceptions;
using xKsef.CredentialService.Endpoints;
using xKsef.CredentialService.Middleware;

namespace xKsef.Tests.Infrastructure;

public sealed class CredentialServiceEnvironmentServerTests
{
    [Theory]
    [InlineData("TEST")]
    [InlineData("PROD")]
    public async Task HealthPreservesExistingFieldsAndReportsStartupEnvironment(string environment)
    {
        using var host = await CreateHost(environment, () => throw new InvalidOperationException("health must not execute credentials"));
        using var client = host.GetTestClient();
        using var response = await client.GetAsync("/internal/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(json.RootElement.GetProperty("success").GetBoolean());
        Assert.Equal("xKsef.Api", json.RootElement.GetProperty("client").GetString());
        Assert.Equal(environment, json.RootElement.GetProperty("ksefEnvironment").GetString());
        Assert.Equal(3, json.RootElement.EnumerateObject().Count());
    }

    [Theory]
    [InlineData("TEST", "TEST", 200)]
    [InlineData("PROD", "PROD", 200)]
    [InlineData("TEST", "PROD", 409)]
    [InlineData("PROD", "TEST", 409)]
    [InlineData("TEST", null, 400)]
    [InlineData("TEST", "", 400)]
    [InlineData("TEST", "DEMO", 400)]
    [InlineData("TEST", "sensitive", 400)]
    [InlineData("TEST", "TEST,PROD", 400)]
    public async Task GuardRunsBeforeCredentialOperation(string serviceEnvironment, string? header, int expectedStatus)
    {
        int operations = 0;
        using var host = await CreateHost(serviceEnvironment, () => operations++);
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/ksef-credentials/authenticate-token");
        if (header != null) request.Headers.TryAddWithoutValidation("X-KSeF-Environment", header);
        using var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, (int)response.StatusCode);
        Assert.Equal(expectedStatus == 200 ? 1 : 0, operations);
        if (expectedStatus != 200)
        {
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(serviceEnvironment, json.RootElement.GetProperty("ksefEnvironment").GetString());
            Assert.Equal(expectedStatus == 409 ? "ksef_environment_mismatch" : "ksef_environment_required", json.RootElement.GetProperty("code").GetString());
            Assert.DoesNotContain("sensitive", await response.Content.ReadAsStringAsync());
        }
    }

    [Fact]
    public async Task MultipleEnvironmentHeadersAreRejected()
    {
        int operations = 0;
        using var host = await CreateHost("TEST", () => operations++);
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/ksef-credentials/token");
        request.Headers.Add("X-KSeF-Environment", new[] { "TEST", "TEST" });
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(0, operations);
    }

    [Theory]
    [InlineData("/internal/health")]
    [InlineData("/internal/ksef-credentials/token")]
    public async Task EnvironmentHeaderDoesNotReplaceAuthentication(string path)
    {
        int operations = 0;
        using var host = await CreateHost("TEST", () => operations++);
        using var client = host.GetTestClient();
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        request.Headers.Add("X-KSeF-Environment", "TEST");
        request.Headers.Add("X-Test-Anonymous", "true");
        using var response = await client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(0, operations);
    }

    [Theory]
    [InlineData("TEST", "PROD")]
    [InlineData("PROD", "TEST")]
    public async Task RestartAfterHealthBlocksActualOperationOnNewService(string apiEnvironment, string restartedEnvironment)
    {
        int operations = 0;
        using var oldHost = await CreateHost(apiEnvironment, () => operations++);
        using var newHost = await CreateHost(restartedEnvironment, () => operations++);
        using var oldTransport = new HttpMessageInvoker(oldHost.GetTestServer().CreateHandler());
        using var newTransport = new HttpMessageInvoker(newHost.GetTestServer().CreateHandler());
        int calls = 0;
        using var client = CredentialServiceEnvironmentTests.CreateHttp(apiEnvironment, new CredentialServiceEnvironmentTests.StubHandler((request, token) =>
        {
            calls++;
            return request.RequestUri!.AbsolutePath == "/internal/health"
                ? oldTransport.SendAsync(request, token)
                : newTransport.SendAsync(request, token);
        }));

        var error = await Assert.ThrowsAsync<CredentialServiceException>(() => client.PostAsJsonAsync("/internal/ksef-credentials/authenticate-certificate", new { Password = "sensitive" }));

        Assert.Equal(CredentialServiceFailure.EnvironmentMismatch, error.Failure);
        Assert.Contains($"API={apiEnvironment}", error.Message);
        Assert.Contains($"CredentialService={restartedEnvironment}", error.Message);
        Assert.DoesNotContain("sensitive", error.Message);
        Assert.Equal(2, calls);
        Assert.Equal(0, operations);
    }

    [Theory]
    [InlineData("mismatch", 503)]
    [InlineData("unavailable", 503)]
    [InlineData("invalid", 502)]
    public async Task ApiExceptionPipelineReturnsSafeDiagnosticWithoutRetry(string failure, int expectedStatus)
    {
        int calls = 0;
        using var dependency = CredentialServiceEnvironmentTests.CreateHttp("TEST", new CredentialServiceEnvironmentTests.StubHandler((_, _) =>
        {
            calls++;
            if (failure == "unavailable") throw new HttpRequestException("sensitive");
            return Task.FromResult(failure == "mismatch"
                ? new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { Success = true, KsefEnvironment = "PROD" }) }
                : new HttpResponseMessage(HttpStatusCode.OK) { Content = JsonContent.Create(new { Sensitive = "sensitive" }) });
        }));
        using var host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddExceptionHandler<xKsef.Api.ExceptionHandlers.CredentialServiceExceptionHandler>();
                services.AddProblemDetails();
            })
            .Configure(app =>
            {
                app.UseExceptionHandler();
                app.Run(async context =>
                {
                    using var response = await dependency.GetAsync("/internal/ksef-credentials/token", context.RequestAborted);
                    await context.Response.WriteAsync("unexpected");
                });
            })).StartAsync();
        using var client = host.GetTestClient();
        using var result = await client.GetAsync("/api/operation");
        Assert.Equal(expectedStatus, (int)result.StatusCode);
        Assert.Equal("application/problem+json", result.Content.Headers.ContentType?.MediaType);
        string body = await result.Content.ReadAsStringAsync();
        Assert.DoesNotContain("sensitive", body);
        Assert.DoesNotContain("unexpected", body);
        Assert.Equal(1, calls);
    }
    private static Task<IHost> CreateHost(string environment, Action execute)
    {
        // TestServer uses only this in-memory pipeline: no Program, SDK warmup, database or TLS connection.
        return new HostBuilder().ConfigureWebHost(web => web.UseTestServer()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
                services.AddAuthorization();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseMiddleware<KsefEnvironmentMiddleware>(environment);
                app.UseEndpoints(endpoints =>
                {
                    endpoints.MapCredentialServiceHealth(environment);
                    endpoints.MapMethods("/internal/ksef-credentials/{**remaining}", new[] { "GET", "POST" }, async context =>
                    {
                        execute();
                        await context.Response.WriteAsJsonAsync(new { Success = true });
                    }).RequireAuthorization();
                });
            })).StartAsync();
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
        : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (Request.Headers.ContainsKey("X-Test-Anonymous")) return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "xKsef.Api") }, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
