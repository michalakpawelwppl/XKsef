using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using xKsef.Api.Controllers;
using xKsef.Api.ExceptionHandlers;
using xKsef.Application.Exceptions;
using xKsef.Application.Credentials;
using xKsef.Application.Invoices;
using xKsef.Domain.Invoices;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Credentials;
using xKsef.Infrastructure.Ksef.Authentication;
using xKsef.Infrastructure.Ksef.Configuration;
using xKsef.Infrastructure.Ksef.Invoices;

namespace xKsef.Tests.Infrastructure;

public sealed class CredentialServiceEnvironmentTests
{
    [Theory]
    [InlineData("TEST")]
    [InlineData("PROD")]
    public async Task EveryClientOperationChecksHealthWithoutCachingAndSendsExpectedHeader(string environment)
    {
        var calls = new List<string>();
        Guid id = Guid.NewGuid();
        using var transport = new StubHandler((request, _) =>
        {
            string path = request.RequestUri!.AbsolutePath;
            calls.Add(path);
            if (path == "/internal/health")
            {
                Assert.False(request.Headers.Contains("X-KSeF-Environment"));
                return Task.FromResult(Json(new { Success = true, Client = "xKsef.Api", KsefEnvironment = environment }));
            }

            Assert.Equal(environment, Assert.Single(request.Headers.GetValues("X-KSeF-Environment")));
            return Task.FromResult(Json(new { CredentialId = id, Exists = true, CredentialType = "Token", AccessToken = "test-access", ValidUntil = DateTimeOffset.UtcNow.AddHours(1) }));
        });
        var services = new ServiceCollection();
        services.AddKsefInfrastructure(new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ksef:Environment"] = environment }).Build());
        services.AddHttpClient("CredentialService", http =>
        {
            http.BaseAddress = new Uri("https://credential.test/");
            http.Timeout = Timeout.InfiniteTimeSpan;
        }).AddHttpMessageHandler<CredentialServiceEnvironmentHandler>().ConfigurePrimaryHttpMessageHandler(() => transport);
        using var provider = services.BuildServiceProvider();
        var client = provider.GetRequiredService<ICredentialServiceClient>();
        Assert.Equal(id, await client.SaveTokenAsync(id, "user", "test-secret", "1234567890"));
        await client.GetTokenInfoAsync(id, "user", "1234567890");
        await client.AuthenticateTokenAsync(id, id, "user", "1234567890");
        await client.AuthenticateCertificateAsync(id, id, "user", "1234567890");
        await client.GetAccessTokenAsync(id, id, "user", "1234567890");
        Assert.Equal(id, await client.SaveCertificateAsync("user", "pem", "key", "password"));
        await client.GetCertificateInfoAsync("user");
        await client.EnsureAuthenticatedAsync(id, id, "user", "1234567890");
        Assert.Equal("Token", await client.GetCredentialTypeAsync(id, id, "user", "0123456789"));

        Assert.Equal(18, calls.Count);
        for (int i = 0; i < calls.Count; i += 2)
        {
            Assert.Equal("/internal/health", calls[i]);
            Assert.StartsWith("/internal/ksef-credentials/", calls[i + 1]);
        }
    }

    [Theory]
    [InlineData("TEST", "PROD")]
    [InlineData("PROD", "TEST")]
    public async Task MismatchBlocksRequestBeforeSendingCredentials(string apiEnvironment, string serviceEnvironment)
    {
        int calls = 0;
        using var http = CreateHttp(apiEnvironment, new StubHandler((request, _) =>
        {
            calls++;
            Assert.Equal("/internal/health", request.RequestUri!.AbsolutePath);
            return Task.FromResult(Json(new { Success = true, KsefEnvironment = serviceEnvironment }));
        }));

        var error = await Assert.ThrowsAsync<CredentialServiceException>(() => http.PostAsJsonAsync("/internal/ksef-credentials/token", new { Token = "sensitive" }));

        Assert.Equal(CredentialServiceFailure.EnvironmentMismatch, error.Failure);
        Assert.Contains($"API={apiEnvironment}", error.Message);
        Assert.Contains($"CredentialService={serviceEnvironment}", error.Message);
        Assert.DoesNotContain("sensitive", error.Message);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData("")]
    [InlineData("null")]
    [InlineData("{}")]
    [InlineData("not-json-sensitive")]
    [InlineData("[]")]
    [InlineData("{\"success\":true}")]
    [InlineData("{\"ksefEnvironment\":\"TEST\"}")]
    [InlineData("{\"success\":false,\"ksefEnvironment\":\"TEST\"}")]
    [InlineData("{\"success\":true,\"ksefEnvironment\":null}")]
    [InlineData("{\"success\":true,\"ksefEnvironment\":\"DEMO\"}")]
    [InlineData("{\"success\":true,\"ksefEnvironment\":\"sensitive\"}")]
    [InlineData("{\"success\":true,\"ksefEnvironment\":42}")]
    public async Task InvalidHealthBlocksOperation(string body)
    {
        int calls = 0;
        using var http = CreateHttp("TEST", new StubHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
        }));

        var error = await Assert.ThrowsAsync<CredentialServiceException>(() => http.GetAsync("/internal/ksef-credentials/token"));
        Assert.Equal(CredentialServiceFailure.InvalidHealthResponse, error.Failure);
        Assert.DoesNotContain("sensitive", error.Message);
        Assert.Equal(1, calls);
    }

    [Fact]
    public async Task UnsupportedHealthContentTypeBlocksOperation()
    {
        using var http = CreateHttp("TEST", new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("{\"success\":true,\"ksefEnvironment\":\"TEST\"}", Encoding.UTF8, "text/plain")
        })));
        var error = await Assert.ThrowsAsync<CredentialServiceException>(() => http.GetAsync("/internal/ksef-credentials/token"));
        Assert.Equal(CredentialServiceFailure.InvalidHealthResponse, error.Failure);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(404)]
    [InlineData(429)]
    [InlineData(500)]
    [InlineData(503)]
    public async Task HealthHttpFailureBlocksOperationWithoutRetry(int status)
    {
        int calls = 0;
        using var http = CreateHttp("TEST", new StubHandler((_, _) =>
        {
            calls++;
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("sensitive") });
        }));
        var error = await Assert.ThrowsAsync<CredentialServiceException>(() => http.GetAsync("/internal/ksef-credentials/token"));
        Assert.Equal(CredentialServiceFailure.Unavailable, error.Failure);
        Assert.DoesNotContain("sensitive", error.Message);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TransportFailureIsSafeAndNeverRetries(bool duringOperation)
    {
        int calls = 0;
        using var http = CreateHttp("TEST", new StubHandler((request, _) =>
        {
            calls++;
            if (duringOperation && request.RequestUri!.AbsolutePath == "/internal/health") return Task.FromResult(Healthy());
            throw new HttpRequestException("sensitive transport details");
        }));
        var error = await Assert.ThrowsAsync<CredentialServiceException>(() => http.PostAsJsonAsync("/internal/ksef-credentials/authenticate-token", new { }));
        Assert.Equal(CredentialServiceFailure.Unavailable, error.Failure);
        Assert.DoesNotContain("sensitive", error.Message);
        Assert.Equal(duringOperation ? 2 : 1, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DeadlineIsReportedAsUnavailableWithoutRetry(bool duringOperation)
    {
        int calls = 0;
        using var http = CreateHttp("TEST", new StubHandler(async (request, cancellation) =>
        {
            calls++;
            if (duringOperation && request.RequestUri!.AbsolutePath == "/internal/health") return Healthy();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellation);
            throw new InvalidOperationException("unreachable");
        }), TimeSpan.FromMilliseconds(100));
        var error = await Assert.ThrowsAsync<CredentialServiceException>(() => http.PostAsJsonAsync("/internal/ksef-credentials/token", new { }));
        Assert.Equal(CredentialServiceFailure.Unavailable, error.Failure);
        Assert.Equal(duringOperation ? 2 : 1, calls);
    }

    [Theory]
    [InlineData("before")]
    [InlineData("health")]
    [InlineData("between")]
    [InlineData("operation")]
    public async Task CallerCancellationRemainsCancellationAndDoesNotRetry(string phase)
    {
        int calls = 0;
        using var cancellation = new CancellationTokenSource();
        using var http = CreateHttp("TEST", new StubHandler((request, token) =>
        {
            calls++;
            bool health = request.RequestUri!.AbsolutePath == "/internal/health";
            if (phase == "between" && health)
            {
                cancellation.Cancel();
                return Task.FromResult(Healthy());
            }
            if ((phase == "health" && health) || (phase == "operation" && !health))
            {
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
            }
            return Task.FromResult(health ? Healthy() : Json(new { }));
        }));
        if (phase == "before") cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.PostAsJsonAsync("/internal/ksef-credentials/token", new { }, cancellation.Token));
        Assert.Equal(phase == "before" ? 0 : phase == "operation" ? 2 : 1, calls);
    }

    [Fact]
    public async Task RestartBetweenSuccessiveRequestsIsDetectedWithoutCachedHealth()
    {
        string environment = "TEST";
        int operations = 0;
        using var http = CreateHttp("TEST", new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/internal/health") return Task.FromResult(Json(new { Success = true, KsefEnvironment = environment }));
            operations++;
            return Task.FromResult(Json(new { }));
        }));
        using var first = await http.GetAsync("/internal/ksef-credentials/token");
        environment = "PROD";

        var error = await Assert.ThrowsAsync<CredentialServiceException>(() => http.GetAsync("/internal/ksef-credentials/token"));
        Assert.Equal(CredentialServiceFailure.EnvironmentMismatch, error.Failure);
        Assert.Equal(1, operations);
    }

    [Fact]
    public async Task HeaderCannotOverrideTheEnvironmentActuallyUsedBySdk()
    {
        using var http = CreateHttp("PROD", new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/internal/health") return Task.FromResult(Json(new { Success = true, KsefEnvironment = "PROD" }));
            Assert.Equal("PROD", Assert.Single(request.Headers.GetValues("X-KSeF-Environment")));
            return Task.FromResult(Json(new { }));
        }));
        using var request = new HttpRequestMessage(HttpMethod.Post, "/internal/ksef-credentials/token");
        request.Headers.Add("X-KSeF-Environment", "TEST");
        using var response = await http.SendAsync(request);
        Assert.True(response.IsSuccessStatusCode);
    }

    [Fact]
    public async Task DependencyFailureDoesNotBlockLocalInvoiceReads()
    {
        int calls = 0;
        using var http = CreateHttp("TEST", new StubHandler((_, _) =>
        {
            calls++;
            throw new HttpRequestException("unavailable");
        }));
        var credentials = new CredentialServiceClient(new SingleClientFactory(http));
        await Assert.ThrowsAsync<CredentialServiceException>(() => credentials.GetTokenInfoAsync(Guid.NewGuid(), "user", "1234567890"));
        var repository = new LocalRepository();
        var service = new KsefInvoiceService(null!, new KsefTokenProvider(credentials, null!), null!, null!, null!, null!, null!, repository);
        var controller = new InvoicesController(service, repository) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
        controller.HttpContext.Items["TenantId"] = Guid.NewGuid();

        var result = Assert.IsType<OkObjectResult>(await controller.GetInvoices(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 31), xKsef.Contracts.Invoices.InvoiceDirection.Sales, CancellationToken.None));

        Assert.Empty(Assert.IsAssignableFrom<IEnumerable<xKsef.Contracts.Invoices.InvoiceDto>>(result.Value));
        Assert.Equal(1, repository.Reads);
        Assert.Equal(1, calls);
    }

    [Theory]
    [InlineData(CredentialServiceFailure.Unavailable, 503)]
    [InlineData(CredentialServiceFailure.InvalidHealthResponse, 502)]
    [InlineData(CredentialServiceFailure.EnvironmentMismatch, 503)]
    public async Task ApiReturnsSafeProblemDetails(CredentialServiceFailure failure, int expectedStatus)
    {
        using var services = new ServiceCollection().AddLogging().BuildServiceProvider();
        var context = new DefaultHttpContext { RequestServices = services };
        using var body = new MemoryStream();
        context.Response.Body = body;
        var error = new CredentialServiceException(failure, "sensitive-api", "sensitive-service");
        Assert.True(await new CredentialServiceExceptionHandler().TryHandleAsync(context, error, CancellationToken.None));
        Assert.Equal(expectedStatus, context.Response.StatusCode);
        Assert.StartsWith("application/problem+json", context.Response.ContentType);
        using var json = JsonDocument.Parse(body.ToArray());
        Assert.Equal(expectedStatus, json.RootElement.GetProperty("status").GetInt32());
        Assert.DoesNotContain("sensitive", Encoding.UTF8.GetString(body.ToArray()));
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    [InlineData(408)]
    [InlineData(500)]
    [InlineData(502)]
    [InlineData(503)]
    [InlineData(504)]
    public async Task OperationHttpFailureIsSafeAndNeverRetries(int status)
    {
        int calls = 0;
        using var http = CreateHttp("TEST", new StubHandler((request, _) =>
        {
            calls++;
            return Task.FromResult(request.RequestUri!.AbsolutePath == "/internal/health"
                ? Healthy()
                : new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent("sensitive") });
        }));
        var error = await Assert.ThrowsAsync<CredentialServiceException>(() => http.PostAsJsonAsync("/internal/ksef-credentials/token", new { }));
        Assert.Equal(CredentialServiceFailure.Unavailable, error.Failure);
        Assert.DoesNotContain("sensitive", error.Message);
        Assert.Equal(2, calls);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BrokenResponseStreamIsUnavailableWithoutRetry(bool duringOperation)
    {
        int calls = 0;
        using var http = CreateHttp("TEST", new StubHandler((request, _) =>
        {
            calls++;
            if (duringOperation && request.RequestUri!.AbsolutePath == "/internal/health") return Task.FromResult(Healthy());
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new BrokenContent() });
        }));
        var error = await Assert.ThrowsAsync<CredentialServiceException>(() => http.GetAsync("/internal/ksef-credentials/token"));
        Assert.Equal(CredentialServiceFailure.Unavailable, error.Failure);
        Assert.DoesNotContain("sensitive", error.Message);
        Assert.Equal(duringOperation ? 2 : 1, calls);
    }

    [Fact]
    public async Task UnrelatedConflictIsNotReportedAsEnvironmentMismatch()
    {
        using var http = CreateHttp("TEST", new StubHandler((request, _) => Task.FromResult(
            request.RequestUri!.AbsolutePath == "/internal/health" ? Healthy()
                : new HttpResponseMessage(HttpStatusCode.Conflict) { Content = new StringContent("business conflict") })));
        using var response = await http.GetAsync("/internal/ksef-credentials/token");
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("business conflict", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task ReloadedConfigurationCannotChangeEnvironmentReportedForAlreadyInitializedSdk()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["Ksef:Environment"] = "TEST" }).Build();
        var services = new ServiceCollection();
        services.AddKsefInfrastructure(configuration);
        configuration["Ksef:Environment"] = "PROD";
        configuration["Ksef:BaseUrl"] = "https://api.ksef.mf.gov.pl";
        using var provider = services.BuildServiceProvider();
        using var handler = provider.GetRequiredService<CredentialServiceEnvironmentHandler>();
        handler.InnerHandler = new StubHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/internal/health") return Task.FromResult(Healthy());
            Assert.Equal("TEST", Assert.Single(request.Headers.GetValues("X-KSeF-Environment")));
            return Task.FromResult(Json(new { }));
        });
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://credential.test/") };
        using var response = await http.GetAsync("/internal/ksef-credentials/token");
        Assert.True(response.IsSuccessStatusCode);
    }

    private sealed class BrokenContent : HttpContent
    {
        public BrokenContent() => Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        protected override Task SerializeToStreamAsync(Stream stream, TransportContext? context) => throw new IOException("sensitive stream failure");
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
    private static HttpResponseMessage Healthy() => Json(new { Success = true, KsefEnvironment = "TEST" });
    private static HttpResponseMessage Json(object value) => new(HttpStatusCode.OK) { Content = JsonContent.Create(value) };

    internal static HttpClient CreateHttp(string environment, HttpMessageHandler transport, TimeSpan? timeout = null)
    {
        var options = Options.Create(new KsefOptions { Environment = "DEMO", BaseUrl = environment == "TEST" ? "https://api-test.ksef.mf.gov.pl" : "https://api.ksef.mf.gov.pl" });
        var handler = new CredentialServiceEnvironmentHandler(options, timeout ?? TimeSpan.FromSeconds(100)) { InnerHandler = transport };
        return new HttpClient(handler) { BaseAddress = new Uri("https://credential.test/"), Timeout = Timeout.InfiniteTimeSpan };
    }

    internal sealed class StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => send(request, cancellationToken);
    }

    private sealed class SingleClientFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class LocalRepository : IInvoiceRepository
    {
        public int Reads { get; private set; }
        public Task<IReadOnlyList<StoredInvoice>> GetInvoicesAsync(Guid tenantId, DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default)
        {
            Reads++;
            return Task.FromResult<IReadOnlyList<StoredInvoice>>([]);
        }
        public Task SaveAsync(StoredInvoice invoice, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<bool> ExistsAsync(Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<StoredInvoice?> GetByKsefNumberAsync(Guid tenantId, string ksefNumber, CancellationToken cancellationToken = default) => throw new NotSupportedException();
    }
}
