using System.Reflection;
using System.Text.Json;
using System.Security.Claims;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Interfaces.Services;
using KSeF.Client.Core.Models.Invoices;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using xKsef.Api.Controllers;
using xKsef.Application.Credentials;
using xKsef.Application.Tenants;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Ksef.Authentication;
using xKsef.Infrastructure.Ksef.Invoices;
using Direction = xKsef.Contracts.Invoices.InvoiceDirection;

namespace xKsef.Tests.Infrastructure;

public sealed class KsefEmptyExportTests
{
    [Theory]
    [InlineData(Direction.Sales)]
    [InlineData(Direction.Purchase)]
    public async Task CompletedEmptyExportReturnsHttp200WithoutDownloadOrDatabaseWork(Direction direction)
    {
        using var setup = new Setup("{\"status\":{\"code\":200},\"package\":{\"invoiceCount\":0,\"size\":0,\"parts\":[],\"isTruncated\":false}}");
        var controller = new InvoiceImportController(null!, setup.Service, new ConfigurationBuilder().Build());
        controller.ControllerContext = new() { HttpContext = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "test-owner") }, "test")) } };
        controller.HttpContext.Items["TenantId"] = setup.Tenant;
        var response = await controller.ImportFromKsefAsync(new(2026, 10, 15), new(2026, 11, 14), direction, default);
        var ok = Assert.IsType<OkObjectResult>(response.Result);
        var result = Assert.IsType<xKsef.Contracts.Invoices.InvoicePackageReadResult>(ok.Value);
        Assert.Equal("REF", result.ReferenceNumber);
        Assert.Equal(0, result.DeclaredInvoiceCount); Assert.Equal(0, result.ReadInvoiceCount);
        Assert.Equal(0, result.ImportedCount); Assert.Equal(0, result.SkippedCount);
        Assert.Equal(direction == Direction.Sales ? InvoiceSubjectType.Subject1 : InvoiceSubjectType.Subject2, setup.Subject);
        Assert.Equal(1, setup.StatusCalls); Assert.Equal(0, setup.Downloads);
    }

    [Theory]
    [InlineData("{\"status\":{\"code\":200}}")]
    [InlineData("{\"status\":{\"code\":200},\"package\":{\"invoiceCount\":0,\"parts\":null}}")]
    [InlineData("{\"status\":{\"code\":200},\"package\":{\"invoiceCount\":1,\"parts\":[]}}")]
    [InlineData("{\"status\":{\"code\":200},\"package\":{\"invoiceCount\":0,\"size\":10,\"parts\":[]}}")]
    [InlineData("{\"status\":{\"code\":200},\"package\":{\"invoiceCount\":0,\"parts\":[],\"isTruncated\":true}}")]
    [InlineData("{\"status\":{\"code\":500},\"package\":{\"invoiceCount\":0,\"parts\":[]}}")]
    public async Task MissingContradictoryTruncatedOrFailedPackageNeverBecomesEmptySuccess(string response)
    {
        using var setup = new Setup(response);
        await Assert.ThrowsAnyAsync<Exception>(() => setup.Service.ImportInvoicesAsync("test-owner", setup.Tenant, new(2026, 10, 15), new(2026, 11, 14), xKsef.Domain.Invoices.InvoiceDirection.Purchase));
        Assert.Equal(0, setup.Downloads);
    }

    [Fact]
    public async Task PendingExportDoesNotBecomeAnEmptyResultAndRemainsCancellable()
    {
        using var cancel = new CancellationTokenSource();
        using var setup = new Setup("{\"status\":{\"code\":100},\"package\":{\"invoiceCount\":0,\"parts\":[]}}", () => cancel.Cancel());
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => setup.Service.ImportInvoicesAsync("test-owner", setup.Tenant, new(2026, 10, 15), new(2026, 11, 14), xKsef.Domain.Invoices.InvoiceDirection.Sales, cancel.Token));
        Assert.Equal(0, setup.Downloads);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task DeclaredPartsMustStillBeDownloadedAndDownloadErrorsMustPropagate(int count)
    {
        var json = "{\"status\":{\"code\":200},\"package\":{\"invoiceCount\":" + count + ",\"size\":1,\"parts\":[{\"ordinalNumber\":1,\"method\":\"GET\",\"url\":\"https://package.invalid/part\",\"expirationDate\":\"2099-01-01T00:00:00Z\"}]}}";
        using var setup = new Setup(json);
        var error = await Assert.ThrowsAsync<InvalidOperationException>(() => setup.Service.ImportInvoicesAsync("test-owner", setup.Tenant, new(2026, 10, 15), new(2026, 11, 14), xKsef.Domain.Invoices.InvoiceDirection.Purchase));
        Assert.Contains("No package should be downloaded", error.Message);
        Assert.Equal(1, setup.Downloads);
    }
    private sealed class Setup : IDisposable
    {
        public Guid Tenant { get; } = Guid.NewGuid();
        public KsefInvoiceService Service { get; }
        public InvoiceSubjectType Subject { get; private set; }
        public int StatusCalls { get; private set; }
        public int Downloads { get; private set; }
        private readonly HttpClient _download;
        public Setup(string response, Action? onStatus = null)
        {
            var credential = Guid.NewGuid();
            var tenants = Proxy<ITenantService>((method, args) => method.Name switch
            {
                "GetTaxEntityAsync" => Task.FromResult<TaxEntity?>(new() { Nip = "0123456789" }),
                "GetPreferredKsefCredentialIdAsync" => Task.FromResult<Guid?>(credential),
                _ => throw new InvalidOperationException("Unexpected tenant operation")
            });
            var credentials = Proxy<ICredentialServiceClient>((method, args) => method.Name switch
            {
                "EnsureAuthenticatedAsync" => Task.CompletedTask,
                "GetAccessTokenAsync" => Task.FromResult(new KsefAccessTokenResponse("synthetic-test-token", DateTimeOffset.UtcNow.AddHours(1))),
                _ => throw new InvalidOperationException("Unexpected credential operation")
            });
            var sdk = Proxy<IKSeFClient>((method, args) =>
            {
                if (method.Name == "ExportInvoicesAsync") { Subject = ((InvoiceExportRequest)args![0]!).Filters.SubjectType; return JsonTask(method.ReturnType, "{\"referenceNumber\":\"REF\"}"); }
                if (method.Name == "GetInvoiceExportStatusAsync") { StatusCalls++; onStatus?.Invoke(); return JsonTask(method.ReturnType, response); }
                throw new InvalidOperationException("Unexpected SDK operation");
            });
            var crypto = Proxy<ICryptographyService>((method, args) => method.Name == "GetEncryptionData" ? Activator.CreateInstance(method.ReturnType) : throw new InvalidOperationException("Unexpected cryptography operation"));
            _download = new HttpClient(new Download(() => Downloads++));
            // Null persistence dependencies make any unintended database processing fail the test.
            Service = new(sdk, new(credentials, tenants), new(), crypto, _download, null!, null!, null!);
        }
        public void Dispose() => _download.Dispose();
    }
    private static object JsonTask(Type taskType, string json)
    {
        var type = taskType.GenericTypeArguments[0];
        return typeof(Task).GetMethod(nameof(Task.FromResult))!.MakeGenericMethod(type).Invoke(null, new[] { JsonSerializer.Deserialize(json, type, JsonSerializerOptions.Web) })!;
    }
    private static T Proxy<T>(Func<MethodInfo, object?[]?, object?> invoke) where T : class
    { var proxy = DispatchProxy.Create<T, LocalProxy>(); ((LocalProxy)(object)proxy).Handler = invoke; return proxy; }
    public class LocalProxy : DispatchProxy
    {
        public Func<MethodInfo, object?[]?, object?> Handler { get; set; } = null!;
        protected override object? Invoke(MethodInfo? method, object?[]? args) => Handler(method!, args);
    }
    private sealed class Download(Action called) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) { called(); throw new InvalidOperationException("No package should be downloaded"); }
    }
}