using System.Net;
using System.Net.Http.Headers;
using System.Text;
using xKsef.Application.Gus;

namespace xKsef.Tests.Infrastructure;

public sealed partial class GusBirClientTests
{
    [Theory]
    [InlineData(200, "text/html", GusBirError.InvalidResponse, GusBirFailureStage.ValidateContentType)]
    [InlineData(500, "text/html", GusBirError.Communication, GusBirFailureStage.ValidateContentType)]
    [InlineData(200, "application/soap+xml", GusBirError.InvalidResponse, GusBirFailureStage.ParseXml)]
    [InlineData(500, "application/soap+xml", GusBirError.Communication, GusBirFailureStage.ParseXml)]
    public async Task ResponseDiagnosticsHttpErrorTakesPriorityOverUnreadableContent(int status, string mediaType, GusBirError expected, GusBirFailureStage stage)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(ResponseDiagnosticReply(status, "<broken" + DiagnosticSentinel, mediaType)));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(expected, error.Error);
        Assert.Equal(status, error.Diagnostics!.HttpStatusCode);
        Assert.Equal(stage, error.Diagnostics.Stage);
        if (status == 500) Assert.Equal(GusBirFailureKind.Http, error.Diagnostics.Kind);
        AssertDiagnosticIsSafe(error);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    public async Task ResponseDiagnosticsSoapFaultRetainsOnlyWhitelistedCodeAndSubcode(int status)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(ResponseDiagnosticReply(status, DiagnosticFault("s:Sender", "a:ActionNotSupported"))));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(GusBirError.ServiceFault, error.Error);
        Assert.Equal(status, error.Diagnostics!.HttpStatusCode);
        Assert.Equal(GusBirFailureKind.SoapFault, error.Diagnostics.Kind);
        Assert.Equal(GusBirFailureStage.ValidateSoap, error.Diagnostics.Stage);
        Assert.Equal(GusBirSoapFaultCode.Sender, error.Diagnostics.SoapFaultCode);
        Assert.Equal(GusBirSoapFaultSubcode.ActionNotSupported, error.Diagnostics.SoapFaultSubcode);
        AssertDiagnosticIsSafe(error);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("s:Receiver", GusBirSoapFaultCode.Receiver)]
    [InlineData("s:VersionMismatch", GusBirSoapFaultCode.VersionMismatch)]
    [InlineData("s:MustUnderstand", GusBirSoapFaultCode.MustUnderstand)]
    [InlineData("s:DataEncodingUnknown", GusBirSoapFaultCode.DataEncodingUnknown)]
    [InlineData("p:Sender", GusBirSoapFaultCode.Other)]
    [InlineData("Sender", GusBirSoapFaultCode.Other)]
    [InlineData("missing:Sender", GusBirSoapFaultCode.Other)]
    [InlineData("s:bad:Sender", GusBirSoapFaultCode.Other)]
    [InlineData(":Sender", GusBirSoapFaultCode.Other)]
    [InlineData("s:", GusBirSoapFaultCode.Other)]
    [InlineData("s: Sender", GusBirSoapFaultCode.Other)]
    public async Task ResponseDiagnosticsFaultCodeRequiresARecognizedExpandedQName(string code, GusBirSoapFaultCode expected)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(ResponseDiagnosticReply(500, DiagnosticFault(code, "a:ActionMismatch"))));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(expected, error.Diagnostics!.SoapFaultCode);
        AssertDiagnosticIsSafe(error);
    }

    [Theory]
    [InlineData("a:DestinationUnreachable", GusBirSoapFaultSubcode.DestinationUnreachable)]
    [InlineData("a:ActionMismatch", GusBirSoapFaultSubcode.ActionMismatch)]
    [InlineData("a:MessageAddressingHeaderRequired", GusBirSoapFaultSubcode.MessageAddressingHeaderRequired)]
    [InlineData("a:InvalidAddressingHeader", GusBirSoapFaultSubcode.InvalidAddressingHeader)]
    [InlineData("a:EndpointUnavailable", GusBirSoapFaultSubcode.EndpointUnavailable)]
    [InlineData("p:ActionNotSupported", GusBirSoapFaultSubcode.Other)]
    [InlineData("missing:ActionNotSupported", GusBirSoapFaultSubcode.Other)]
    public async Task ResponseDiagnosticsFaultSubcodeRequiresAWhitelistedAddressingQName(string subcode, GusBirSoapFaultSubcode expected)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(ResponseDiagnosticReply(500, DiagnosticFault("s:Receiver", subcode))));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(expected, error.Diagnostics!.SoapFaultSubcode);
        AssertDiagnosticIsSafe(error);
    }

    [Fact]
    public async Task ResponseDiagnosticsArbitraryFaultValuesAreOtherAndNeverRetained()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(ResponseDiagnosticReply(500, DiagnosticFault("s:" + DiagnosticSentinel, "a:" + DiagnosticSentinel))));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(GusBirSoapFaultCode.Other, error.Diagnostics!.SoapFaultCode);
        Assert.Equal(GusBirSoapFaultSubcode.Other, error.Diagnostics.SoapFaultSubcode);
        AssertDiagnosticIsSafe(error);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    public async Task ResponseDiagnosticsMtomSoapFaultKeepsHttpStatusAndSafeCodes(int status)
    {
        var body = "--boundary\r\nContent-Type: application/xop+xml; charset=utf-8\r\nContent-ID: <root>\r\n\r\n"
            + DiagnosticFault("s:Receiver", "a:EndpointUnavailable") + "\r\n--boundary--\r\n";
        var content = new StringContent(body, Encoding.UTF8);
        content.Headers.ContentType = MediaTypeHeaderValue.Parse("multipart/related; boundary=boundary; start=\"<root>\"; type=\"application/xop+xml\"");
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content }));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(GusBirError.ServiceFault, error.Error);
        Assert.Equal(status, error.Diagnostics!.HttpStatusCode);
        Assert.Equal(GusBirContentType.MultipartRelated, error.Diagnostics.ContentType);
        Assert.Equal(GusBirFailureStage.ValidateSoap, error.Diagnostics.Stage);
        Assert.Equal(GusBirSoapFaultCode.Receiver, error.Diagnostics.SoapFaultCode);
        Assert.Equal(GusBirSoapFaultSubcode.EndpointUnavailable, error.Diagnostics.SoapFaultSubcode);
        AssertDiagnosticIsSafe(error);
    }

    [Fact]
    public async Task ResponseDiagnosticsMissingFaultCodePreservesExistingFaultRecognition()
    {
        var body = $"<s:Envelope xmlns:s=\"{Soap}\"><s:Body><s:Fault><s:Reason><s:Text>{DiagnosticSentinel}</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>";
        using var handler = new StubHandler((_, _) => Task.FromResult(ResponseDiagnosticReply(500, body)));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(GusBirError.ServiceFault, error.Error);
        Assert.Equal(GusBirSoapFaultCode.None, error.Diagnostics!.SoapFaultCode);
        Assert.Equal(GusBirSoapFaultSubcode.None, error.Diagnostics.SoapFaultSubcode);
        AssertDiagnosticIsSafe(error);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    public async Task ResponseDiagnosticsInvalidSoapStructuresAreDistinctFromXmlSyntaxErrors(int status)
    {
        var bodies = new[]
        {
            "<other/>",
            $"<s:Envelope xmlns:s=\"{Soap}\"/>",
            $"<s:Envelope xmlns:s=\"{Soap}\"><s:Body/><s:Body/></s:Envelope>",
            $"<s:Envelope xmlns:s=\"{Soap}\"><s:Body><a/><b/></s:Body></s:Envelope>",
            $"<s:Envelope xmlns:s=\"{Soap}\"><s:Body><ZalogujResponse/></s:Body></s:Envelope>",
            $"<s:Envelope xmlns:s=\"{Soap}\"><s:Body><WylogujResponse xmlns=\"{Service}\"/></s:Body></s:Envelope>",
            $"<s:Envelope xmlns:s=\"{Soap}\"><s:Body><ZalogujResponse xmlns=\"{Service}\"/></s:Body></s:Envelope>",
            $"<s:Envelope xmlns:s=\"{Soap}\"><s:Body><ZalogujResponse xmlns=\"{Service}\"><ZalogujResult/><ZalogujResult/></ZalogujResponse></s:Body></s:Envelope>",
            $"<s:Envelope xmlns:s=\"{Soap}\"><s:Body><ZalogujResponse xmlns=\"{Service}\"><ZalogujResult><nested/></ZalogujResult></ZalogujResponse></s:Body></s:Envelope>",
            $"<s:Envelope xmlns:s=\"{Soap}\"><s:Body><Fault xmlns=\"urn:untrusted\"/></s:Body></s:Envelope>"
        };
        foreach (var body in bodies)
        {
            using var handler = new StubHandler((_, _) => Task.FromResult(ResponseDiagnosticReply(status, body)));
            var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
            Assert.Equal(status == 200 ? GusBirError.InvalidResponse : GusBirError.Communication, error.Error);
            Assert.Equal(status, error.Diagnostics!.HttpStatusCode);
            Assert.Equal(GusBirFailureStage.ValidateSoap, error.Diagnostics.Stage);
            Assert.Equal(status == 200 ? GusBirFailureKind.SoapValidation : GusBirFailureKind.Http, error.Diagnostics.Kind);
            AssertDiagnosticIsSafe(error);
        }
    }

    [Theory]
    [InlineData(200, "missing-boundary", GusBirFailureStage.ParseMtom)]
    [InlineData(500, "missing-boundary", GusBirFailureStage.ParseMtom)]
    [InlineData(200, "long-boundary", GusBirFailureStage.ParseMtom)]
    [InlineData(200, "truncated", GusBirFailureStage.ParseMtom)]
    [InlineData(500, "truncated", GusBirFailureStage.ParseMtom)]
    [InlineData(200, "missing-root", GusBirFailureStage.ParseMtom)]
    [InlineData(200, "non-xml-root", GusBirFailureStage.ParseMtom)]
    [InlineData(200, "invalid-xml", GusBirFailureStage.ParseXml)]
    [InlineData(500, "invalid-xml", GusBirFailureStage.ParseXml)]
    public async Task ResponseDiagnosticsMalformedMtomIsNotMistakenForNetworkFailure(int status, string kind, GusBirFailureStage stage)
    {
        var content = new StringContent(kind == "truncated" ? "--boundary\r\nContent-Type: application/xop+xml\r\n\r\n<broken"
            : "--boundary\r\nContent-Type: " + (kind == "non-xml-root" ? "text/html" : "application/xop+xml") + "\r\nContent-ID: <root>\r\n\r\n<broken\r\n--boundary--\r\n", Encoding.UTF8);
        content.Headers.ContentType = new MediaTypeHeaderValue("multipart/related");
        if (kind != "missing-boundary") content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("boundary", kind == "long-boundary" ? new string('b', 129) : "boundary"));
        if (kind == "missing-root") content.Headers.ContentType.Parameters.Add(new NameValueHeaderValue("start", "\"<missing>\""));
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status) { Content = content }));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(status == 200 ? GusBirError.InvalidResponse : GusBirError.Communication, error.Error);
        Assert.Equal(stage, error.Diagnostics!.Stage);
        Assert.Equal(status, error.Diagnostics.HttpStatusCode);
        Assert.Equal(GusBirContentType.MultipartRelated, error.Diagnostics.ContentType);
        Assert.Equal(status == 500 ? GusBirFailureKind.Http : stage == GusBirFailureStage.ParseXml ? GusBirFailureKind.Xml : GusBirFailureKind.Mtom, error.Diagnostics.Kind);
        AssertDiagnosticIsSafe(error);
    }

    [Theory]
    [InlineData(401, GusBirError.Communication)]
    [InlineData(403, GusBirError.Communication)]
    [InlineData(429, GusBirError.Unavailable)]
    [InlineData(502, GusBirError.Unavailable)]
    [InlineData(503, GusBirError.Unavailable)]
    [InlineData(504, GusBirError.Unavailable)]
    public async Task ResponseDiagnosticsSpecialHttpErrorsNeverReadOrParseTheBody(int status, GusBirError expected)
    {
        var stream = new ExplosiveDiagnosticStream();
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)
        {
            Content = new StreamContent(stream)
        }));
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
        Assert.Equal(expected, error.Error);
        Assert.Equal(status, error.Diagnostics!.HttpStatusCode);
        Assert.Equal(GusBirFailureStage.ValidateHttpResponse, error.Diagnostics.Stage);
        Assert.Equal(0, stream.ReadCalls);
        Assert.Single(handler.Requests);
        AssertDiagnosticIsSafe(error);
    }

    [Fact]
    public async Task ResponseDiagnosticsValidLoginPayloadOnHttp500DoesNotCreateASession()
    {
        using var handler = new StubHandler((request, _) =>
        {
            var reply = Reply(request.Operation, Sid);
            reply.StatusCode = HttpStatusCode.InternalServerError;
            return Task.FromResult(reply);
        });
        var client = Client(new StubHttpClientFactory(handler));
        var error = await Assert.ThrowsAsync<GusBirException>(() => client.LoginAsync());
        Assert.Equal(GusBirError.Communication, error.Error);
        Assert.Equal(500, error.Diagnostics!.HttpStatusCode);
        await client.LogoutAsync();
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(500)]
    public async Task ResponseDiagnosticsDtdAndResponseSizeProtectionRemainEnabled(int status)
    {
        var bodies = new[]
        {
            $"<!DOCTYPE Envelope [<!ENTITY injected SYSTEM 'file:///must-not-be-read'>]><s:Envelope xmlns:s=\"{Soap}\">&injected;</s:Envelope>",
            new string('x', 2 * 1024 * 1024 + 1)
        };
        foreach (var body in bodies)
        {
            using var handler = new StubHandler((_, _) => Task.FromResult(ResponseDiagnosticReply(status, body)));
            var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).LoginAsync());
            Assert.Equal(status == 200 ? GusBirError.InvalidResponse : GusBirError.Communication, error.Error);
            Assert.Equal(status, error.Diagnostics!.HttpStatusCode);
            AssertDiagnosticIsSafe(error);
            Assert.DoesNotContain("must-not-be-read", error.ToString());
        }
    }

    [Theory]
    [InlineData(200, false)]
    [InlineData(202, false)]
    [InlineData(200, true)]
    [InlineData(202, true)]
    public async Task ResponseDiagnosticsEmbeddedResultXmlKeepsActualHttpStatus(int status, bool invalidStructure)
    {
        using var handler = new StubHandler((request, _) =>
        {
            if (request.Operation == "Zaloguj") return Task.FromResult(Reply(request.Operation, Sid));
            var reply = Reply(request.Operation, invalidStructure ? "<other/>" : "<broken" + DiagnosticSentinel);
            reply.StatusCode = (HttpStatusCode)status;
            return Task.FromResult(reply);
        });
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(new StubHttpClientFactory(handler)).SearchByNipAsync("8567346215"));
        Assert.Equal(GusBirError.InvalidResponse, error.Error);
        Assert.Equal(status, error.Diagnostics!.HttpStatusCode);
        Assert.Equal(invalidStructure ? GusBirFailureStage.ValidateResult : GusBirFailureStage.ParseXml, error.Diagnostics.Stage);
        AssertDiagnosticIsSafe(error);
        Assert.Equal(2, handler.Requests.Count);
    }

    private static HttpResponseMessage ResponseDiagnosticReply(int status, string body, string mediaType = "application/soap+xml")
        => new((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, mediaType) };

    private static string DiagnosticFault(string code, string subcode)
        => $"<s:Envelope xmlns:s=\"{Soap}\" xmlns:a=\"http://www.w3.org/2005/08/addressing\" xmlns:p=\"urn:{DiagnosticSentinel}\"><s:Body><s:Fault><s:Code><s:Value>{code}</s:Value><s:Subcode><s:Value>{subcode}</s:Value></s:Subcode></s:Code><s:Reason><s:Text>{DiagnosticSentinel}</s:Text></s:Reason><s:Detail>{DiagnosticSentinel}</s:Detail></s:Fault></s:Body></s:Envelope>";

    private sealed class ExplosiveDiagnosticStream : MemoryStream
    {
        public int ReadCalls { get; private set; }
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            ReadCalls++;
            throw new IOException(DiagnosticSentinel);
        }
    }
}
