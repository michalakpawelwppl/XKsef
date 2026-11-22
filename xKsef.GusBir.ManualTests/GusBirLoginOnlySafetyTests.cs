using System.Net;
using System.Net.Http.Headers;
using System.Text;
using xKsef.Application.Gus;
using xKsef.Infrastructure.Gus;
using Xunit.Abstractions;

namespace xKsef.GusBir.ManualTests;

/// <summary>Offline tests only: every HTTP pipeline ends in a synthetic handler, never a network transport.</summary>
public sealed class GusBirLoginOnlySafetyTests
{
    [Fact]
    public async Task RuntimeOptInIsRequiredEvenIfTheFactAttributeIsBypassed()
    {
        using var transport = new CountingTransport();
        using var guard = new GusBirLoginOnlyGuard(() => false) { InnerHandler = transport };
        using var http = new HttpClient(guard, disposeHandler: false);
        using var request = LoginRequest();
        await Assert.ThrowsAsync<InvalidOperationException>(() => http.SendAsync(request));
        Assert.Equal(0, transport.Requests);
        Assert.Equal(0, guard.Requests);
    }

    [Fact]
    public async Task ASecondRequestIsRejectedEvenAfterTheFirstTransportAttemptFailed()
    {
        using var transport = new CountingTransport(fail: true);
        using var guard = new GusBirLoginOnlyGuard(() => true) { InnerHandler = transport };
        using var http = new HttpClient(guard, disposeHandler: false);
        using var first = LoginRequest();
        using var second = LoginRequest();
        await Assert.ThrowsAsync<HttpRequestException>(() => http.SendAsync(first));
        await Assert.ThrowsAsync<InvalidOperationException>(() => http.SendAsync(second));
        Assert.Equal(1, transport.Requests);
        Assert.Equal(1, guard.Requests);
    }

    [Fact]
    public async Task ConcurrentAttemptsForwardAtMostOneRequest()
    {
        using var transport = new CountingTransport();
        using var guard = new GusBirLoginOnlyGuard(() => true) { InnerHandler = transport };
        using var http = new HttpClient(guard, disposeHandler: false);
        var attempts = Enumerable.Range(0, 8).Select(async _ =>
        {
            using var request = LoginRequest();
            try { using var response = await http.SendAsync(request); return true; }
            catch (InvalidOperationException) { return false; }
        });
        Assert.Single((await Task.WhenAll(attempts)).Where(succeeded => succeeded));
        Assert.Equal(1, transport.Requests);
        Assert.Equal(1, guard.Requests);
    }

    [Theory]
    [InlineData("production")]
    [InlineData("get")]
    [InlineData("search")]
    [InlineData("logout")]
    [InlineData("sid")]
    [InlineData("missing-action")]
    [InlineData("duplicate-action")]
    [InlineData("wrong-content-type")]
    public async Task OnlyTestLoginWithoutASessionHeaderCanReachTheTransport(string invalid)
    {
        using var transport = new CountingTransport();
        using var guard = new GusBirLoginOnlyGuard(() => true) { InnerHandler = transport };
        using var http = new HttpClient(guard, disposeHandler: false);
        using var request = LoginRequest();
        switch (invalid)
        {
            case "production": request.RequestUri = new Uri(GusBirOptions.ProductionServiceUrl); break;
            case "get": request.Method = HttpMethod.Get; break;
            case "search": SetAction(request, "DaneSzukajPodmioty"); break;
            case "logout": SetAction(request, "Wyloguj"); break;
            case "sid": request.Headers.Add("sid", "synthetic-session"); break;
            case "missing-action": request.Content!.Headers.ContentType!.Parameters.Clear(); break;
            case "duplicate-action": request.Content!.Headers.ContentType!.Parameters.Add(new NameValueHeaderValue("action", "\"duplicate\"")); break;
            case "wrong-content-type": request.Content!.Headers.ContentType!.MediaType = "text/xml"; break;
        }
        await Assert.ThrowsAsync<InvalidOperationException>(() => http.SendAsync(request));
        Assert.Equal(0, transport.Requests);
        Assert.Equal(0, guard.Requests);
    }

    [Fact]
    public async Task CancelledRequestDoesNotConsumeOrSendTheSingleAttempt()
    {
        using var transport = new CountingTransport();
        using var guard = new GusBirLoginOnlyGuard(() => true) { InnerHandler = transport };
        using var http = new HttpClient(guard, disposeHandler: false);
        using var request = LoginRequest();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => http.SendAsync(request, cancellation.Token));
        Assert.Equal(0, transport.Requests);
        Assert.Equal(0, guard.Requests);
    }

    [Fact]
    public void ReportContainsOnlyFiveControlledResultFields()
    {
        var output = new RecordingOutput();
        var diagnostic = new GusBirDiagnostics(GusBirFailureKind.Tls, GusBirFailureStage.SendRequest, HttpRequestError.SecureConnectionError);
        GusBirLoginOnlyReport.Write(output, false, GusBirError.Communication, diagnostic, 1);
        Assert.Equal(5, output.Lines.Count);
        Assert.Equal("Logowanie: niepowodzenie", output.Lines[0]);
        Assert.Equal("GusBirError: Communication", output.Lines[1]);
        Assert.Contains("SecureConnectionError", output.Lines[2]);
        Assert.Equal("Liczba żądań HTTP: 1", output.Lines[3]);
        Assert.Equal("Uzyskano SID: nie", output.Lines[4]);
    }

    private static HttpRequestMessage LoginRequest()
    {
        var request = new HttpRequestMessage(HttpMethod.Post, GusBirOptions.TestServiceUrl)
        {
            Content = new StringContent("synthetic-body", Encoding.UTF8, "application/soap+xml")
        };
        SetAction(request, "Zaloguj");
        return request;
    }

    private static void SetAction(HttpRequestMessage request, string operation)
    {
        request.Content!.Headers.ContentType!.Parameters.Clear();
        request.Content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("action", "\"http://CIS/BIR/PUBL/2014/07/IUslugaBIRzewnPubl/" + operation + "\""));
    }

    private sealed class CountingTransport(bool fail = false) : HttpMessageHandler
    {
        private int _requests;
        public int Requests => Volatile.Read(ref _requests);
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _requests);
            if (fail) throw new HttpRequestException("Synthetic transport failure.");
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent));
        }
    }

    private sealed class RecordingOutput : ITestOutputHelper
    {
        public List<string> Lines { get; } = [];
        public void WriteLine(string message) => Lines.Add(message);
        public void WriteLine(string format, params object[] args) => Lines.Add(string.Format(format, args));
    }
}
