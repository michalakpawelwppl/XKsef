using System.ComponentModel;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using xKsef.Application.Gus;
using xKsef.Infrastructure.Gus;

namespace xKsef.Tests.Infrastructure;

public sealed partial class GusBirClientTests
{
    private const string DiagnosticSentinel = "sensitive-diagnostic-test-payload";

    [Fact]
    public void DiagnosticsOriginalExceptionConstructorRemainsCompatible()
    {
        var error = new GusBirException(GusBirError.ServiceRejected, "GetValue", 2);
        Assert.Equal(GusBirError.ServiceRejected, error.Error);
        Assert.Equal("GetValue", error.Operation);
        Assert.Equal(2, error.ServiceCode);
        Assert.Null(error.Diagnostics);
        Assert.Null(error.InnerException);
    }

    [Theory]
    [InlineData(HttpRequestError.NameResolutionError, GusBirFailureKind.Dns)]
    [InlineData(HttpRequestError.ConnectionError, GusBirFailureKind.Connection)]
    [InlineData(HttpRequestError.SecureConnectionError, GusBirFailureKind.Tls)]
    [InlineData(HttpRequestError.ProxyTunnelError, GusBirFailureKind.Unknown)]
    public async Task DiagnosticsTransportUsesTypedHttpErrorWithoutRetainingException(HttpRequestError httpError, GusBirFailureKind kind)
    {
        var original = new HttpRequestException(httpError, DiagnosticSentinel);
        original.Data["secret"] = DiagnosticSentinel;
        using var handler = new StubHandler((_, _) => throw original);
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(GusBirError.Communication, error.Error);
        var diagnostic = Assert.IsType<GusBirDiagnostics>(error.Diagnostics);
        Assert.Equal(kind, diagnostic.Kind);
        Assert.Equal(httpError, diagnostic.HttpRequestError);
        Assert.Equal(GusBirFailureStage.SendRequest, diagnostic.Stage);
        Assert.Equal(GusBirSystemErrorKind.HResult, diagnostic.SystemErrorKind);
        Assert.Equal(original.HResult, diagnostic.SystemErrorCode);
        Assert.Null(diagnostic.HttpStatusCode);
        Assert.Equal(GusBirContentType.Missing, diagnostic.ContentType);
        AssertDiagnosticIsSafe(error);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(SocketError.HostNotFound, GusBirFailureKind.Dns)]
    [InlineData(SocketError.ConnectionRefused, GusBirFailureKind.Connection)]
    [InlineData(SocketError.InvalidArgument, GusBirFailureKind.Unknown)]
    public async Task DiagnosticsSocketFallbackUsesOnlyTypedNumericCode(SocketError code, GusBirFailureKind kind)
    {
        using var handler = new StubHandler((_, _) => throw new HttpRequestException(DiagnosticSentinel, new SocketException((int)code)));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(kind, error.Diagnostics!.Kind);
        Assert.Equal(GusBirSystemErrorKind.Socket, error.Diagnostics.SystemErrorKind);
        Assert.Equal((int)code, error.Diagnostics.SystemErrorCode);
        AssertDiagnosticIsSafe(error);
    }

    [Fact]
    public async Task DiagnosticsTlsRetainsNativeCodeButNotAuthenticationMessage()
    {
        const int nativeCode = -2146893042;
        var original = new HttpRequestException(HttpRequestError.SecureConnectionError, DiagnosticSentinel,
            new AuthenticationException(DiagnosticSentinel, new Win32Exception(nativeCode, DiagnosticSentinel)));
        using var handler = new StubHandler((_, _) => throw original);
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(GusBirFailureKind.Tls, error.Diagnostics!.Kind);
        Assert.Equal(GusBirSystemErrorKind.Win32, error.Diagnostics.SystemErrorKind);
        Assert.Equal(nativeCode, error.Diagnostics.SystemErrorCode);
        AssertDiagnosticIsSafe(error);
    }

    [Fact]
    public async Task DiagnosticsUnknownTransportDoesNotInferDnsOrTlsFromMessage()
    {
        using var handler = new StubHandler((_, _) => throw new HttpRequestException("DNS TLS certificate " + DiagnosticSentinel));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(GusBirFailureKind.Unknown, error.Diagnostics!.Kind);
        AssertDiagnosticIsSafe(error);
    }

    [Theory]
    [InlineData(401, GusBirError.Communication)]
    [InlineData(403, GusBirError.Communication)]
    [InlineData(429, GusBirError.Unavailable)]
    [InlineData(502, GusBirError.Unavailable)]
    [InlineData(503, GusBirError.Unavailable)]
    [InlineData(504, GusBirError.Unavailable)]
    public async Task DiagnosticsHttpStatusAndSafeMediaTypeDoNotBecomeKeyRejection(int status, GusBirError expected)
    {
        using var handler = new StubHandler((_, _) =>
        {
            var content = new StringContent(DiagnosticSentinel, Encoding.UTF8, "text/html");
            content.Headers.ContentType!.Parameters.Add(new NameValueHeaderValue("private", DiagnosticSentinel));
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content });
        });
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(expected, error.Error);
        Assert.Equal(GusBirFailureKind.Http, error.Diagnostics!.Kind);
        Assert.Equal(GusBirFailureStage.ValidateHttpResponse, error.Diagnostics.Stage);
        Assert.Equal(status, error.Diagnostics.HttpStatusCode);
        Assert.Equal(GusBirContentType.Html, error.Diagnostics.ContentType);
        AssertDiagnosticIsSafe(error);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task DiagnosticsUnknownMediaTypeNeverStoresAnArbitraryHeaderValue()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden)
        {
            Content = new StringContent(DiagnosticSentinel, Encoding.UTF8, "application/" + DiagnosticSentinel)
        }));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(GusBirContentType.Other, error.Diagnostics!.ContentType);
        AssertDiagnosticIsSafe(error);
    }

    [Theory]
    [InlineData(200, "text/html", "html", GusBirFailureStage.ValidateContentType)]
    [InlineData(500, "text/html", "html", GusBirFailureStage.ValidateContentType)]
    [InlineData(500, "application/soap+xml", "xml", GusBirFailureStage.ParseXml)]
    [InlineData(500, "multipart/related", "mtom", GusBirFailureStage.ParseMtom)]
    public async Task DiagnosticsParserFailureRetainsReceivedHttpStatus(int status, string contentType, string body, GusBirFailureStage stage)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StringContent(body + DiagnosticSentinel, Encoding.UTF8, contentType)
        }));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(status, error.Diagnostics!.HttpStatusCode);
        Assert.Equal(stage, error.Diagnostics.Stage);
        AssertDiagnosticIsSafe(error);
    }

    [Fact]
    public async Task DiagnosticsSoapFaultRetainsHttpStatusWithoutAnyRemoteFaultTextOrCodes()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($"<s:Envelope xmlns:s=\"{Soap}\"><s:Body><s:Fault><s:Code><s:Value>{DiagnosticSentinel}</s:Value><s:Subcode><s:Value>{DiagnosticSentinel}</s:Value></s:Subcode></s:Code><s:Reason><s:Text>{DiagnosticSentinel}</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>", Encoding.UTF8, "application/soap+xml")
        }));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(GusBirError.ServiceFault, error.Error);
        Assert.Equal(GusBirFailureKind.SoapFault, error.Diagnostics!.Kind);
        Assert.Equal(500, error.Diagnostics.HttpStatusCode);
        Assert.Equal(GusBirContentType.SoapXml, error.Diagnostics.ContentType);
        AssertDiagnosticIsSafe(error);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    public async Task DiagnosticsInterruptedReadIsCommunicationAndRetainsResponseMetadata(int status)
    {
        using var handler = new StubHandler((_, _) =>
        {
            var content = new StreamContent(new InterruptedDiagnosticStream());
            content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml");
            return Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content });
        });
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(GusBirError.Communication, error.Error);
        Assert.Equal(GusBirFailureKind.ResponseRead, error.Diagnostics!.Kind);
        Assert.Equal(GusBirFailureStage.ReadResponse, error.Diagnostics.Stage);
        Assert.Equal(HttpRequestError.ResponseEnded, error.Diagnostics.HttpRequestError);
        Assert.Equal(status, error.Diagnostics.HttpStatusCode);
        Assert.Equal(GusBirContentType.SoapXml, error.Diagnostics.ContentType);
        AssertDiagnosticIsSafe(error);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(401)]
    [InlineData(403)]
    public async Task DiagnosticsHttpAuthenticationFailureInvalidatesSidWithoutAutomaticRetry(int status)
    {
        var searches = 0;
        using var handler = new StubHandler((request, _) => Task.FromResult(request.Operation == "Zaloguj" ? Reply("Zaloguj", Sid)
            : searches++ == 0 ? new HttpResponseMessage((HttpStatusCode)status) : Reply(request.Operation, EntityXml)));
        var client = Client(new StubHttpClientFactory(handler));
        var error = await Assert.ThrowsAsync<GusBirException>(() => client.SearchByNipAsync("8567346215"));
        Assert.Equal(GusBirError.Communication, error.Error);
        Assert.Equal(status, error.Diagnostics!.HttpStatusCode);
        Assert.Single(handler.Requests, request => request.Operation == "Zaloguj");
        Assert.Single(handler.Requests, request => request.Operation == "DaneSzukajPodmioty");
        Assert.Single(await client.SearchByNipAsync("8567346215"));
        Assert.Equal(2, handler.Requests.Count(request => request.Operation == "Zaloguj"));
        Assert.Equal(2, handler.Requests.Count(request => request.Operation == "DaneSzukajPodmioty"));
    }

    [Fact]
    public async Task DiagnosticsEmptyApplicationLoginResultRemainsAuthentication()
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, "")));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(GusBirError.Authentication, error.Error);
        Assert.Null(error.Diagnostics);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiagnosticsTimeoutIdentifiesSendOrReadStage(bool reading)
    {
        using var handler = new StubHandler(async (_, token) =>
        {
            if (!reading) await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StreamContent(new WaitingDiagnosticStream()) };
        });
        var client = new GusBirClient(new StubHttpClientFactory(handler), Options.Create(new GusBirOptions { TimeoutSeconds = 1 }), TimeProvider.System, CreateScopeFactory());
        var error = await Assert.ThrowsAsync<GusBirException>(() => client.LoginAsync());
        Assert.Equal(GusBirError.Timeout, error.Error);
        Assert.Equal(GusBirFailureKind.Timeout, error.Diagnostics!.Kind);
        Assert.Equal(reading ? GusBirFailureStage.ReadResponse : GusBirFailureStage.SendRequest, error.Diagnostics.Stage);
        Assert.Equal(reading ? 200 : (int?)null, error.Diagnostics.HttpStatusCode);
        AssertDiagnosticIsSafe(error);
    }

    [Fact]
    public async Task DiagnosticsCallerCancellationDuringReadStillPropagatesCancellation()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new WaitingDiagnosticStream(entered))
        }));
        var client = Client(new StubHttpClientFactory(handler));
        using var cancellation = new CancellationTokenSource();
        var pending = client.LoginAsync(cancellation.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Single(handler.Requests);
    }

    private static void AssertDiagnosticIsSafe(GusBirException error)
    {
        Assert.Null(error.InnerException);
        Assert.Empty(error.Data);
        Assert.DoesNotContain(DiagnosticSentinel, error.ToString());
        Assert.DoesNotContain(DiagnosticSentinel, JsonSerializer.Serialize(error.Diagnostics));
        Assert.DoesNotContain(DiagnosticSentinel, error.Diagnostics?.ToString() ?? "");
    }

    private sealed class InterruptedDiagnosticStream() : MemoryStream(Encoding.UTF8.GetBytes("<s:Envelope"))
    {
        private bool _read;
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_read) throw new HttpIOException(HttpRequestError.ResponseEnded, DiagnosticSentinel);
            _read = true;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }

    private sealed class WaitingDiagnosticStream(TaskCompletionSource? entered = null) : MemoryStream
    {
        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            entered?.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return 0;
        }
    }
}
