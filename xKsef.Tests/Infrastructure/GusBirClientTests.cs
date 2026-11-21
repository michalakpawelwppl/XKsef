using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Application.Gus;
using xKsef.Infrastructure.Gus;

namespace xKsef.Tests.Infrastructure;

public sealed partial class GusBirClientTests : IDisposable
{
    private readonly List<ServiceProvider> _providers = [];
    private const string Sid = "testSession123456789";
    private const string Service = "http://CIS/BIR/PUBL/2014/07";
    private const string Diagnostic = "http://CIS/BIR/2014/07";
    private const string Data = "http://CIS/BIR/PUBL/2014/07/DataContract";
    private const string Soap = "http://www.w3.org/2003/05/soap-envelope";
    private const string EntityXml = "<root><dane><Regon>012345675</Regon><Nip>8567346215</Nip><Nazwa>Żółta &amp; Zielona spółka</Nazwa><Wojewodztwo>WIELKOPOLSKIE</Wojewodztwo><Powiat>Poznań</Powiat><Gmina>Poznań</Gmina><Miejscowosc>Poznań</Miejscowosc><KodPocztowy>60-001</KodPocztowy><Ulica>Kwiatowa</Ulica><NrNieruchomosci>12</NrNieruchomosci><NrLokalu>3</NrLokalu><MiejscowoscPoczty>Poznań</MiejscowoscPoczty><Typ>P</Typ><SilosID>6</SilosID></dane></root>";

    [Theory]
    [InlineData("Nip", " 8567346215 ", "8567346215")]
    [InlineData("Regon", " 012345675 ", "012345675")]
    [InlineData("Regon", "12345678512347", "12345678512347")]
    [InlineData("Krs", " 0000123456 ", "0000123456")]
    public async Task SearchUsesDocumentedSoapNamespacesSidAndExactlyOneNormalizedCriterion(string kind, string input, string expected)
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : EntityXml)));
        var http = new StubHttpClientFactory(handler);
        var client = Client(http);
        var result = Assert.Single(await Search(client, kind, input));
        Assert.Equal(2, handler.Requests.Count);
        var login = handler.Requests[0];
        Assert.Null(login.Sid);
        Assert.Equal(GusBirOptions.PublicTestApiKey, login.Body.Descendants(XName.Get("pKluczUzytkownika", Service)).Single().Value);
        var search = handler.Requests[1];
        Assert.Equal(Sid, search.Sid);
        Assert.Equal("application/soap+xml", search.ContentType.MediaType);
        Assert.Equal("utf-8", search.ContentType.CharSet);
        XNamespace addressing = "http://www.w3.org/2005/08/addressing";
        Assert.Equal(Service + "/IUslugaBIRzewnPubl/DaneSzukajPodmioty", search.Body.Descendants(addressing + "Action").Single().Value);
        Assert.Equal(GusBirOptions.TestServiceUrl, search.Body.Descendants(addressing + "To").Single().Value);
        var criterion = Assert.Single(search.Body.Descendants(XName.Get("pParametryWyszukiwania", Service)).Single().Elements());
        Assert.Equal(XName.Get(kind, Data), criterion.Name);
        Assert.Equal(expected, criterion.Value);
        Assert.Equal("012345675", result.Regon);
        Assert.Equal("8567346215", result.Nip);
        Assert.Null(result.KrsNumber);
        Assert.Equal("Żółta & Zielona spółka", result.FullName);
        Assert.Equal("P", result.EntityType);
        Assert.Equal(6, result.SiloId);
        Assert.Equal("WIELKOPOLSKIE", result.Address.Voivodeship);
        Assert.Equal("Poznań", result.Address.County);
        Assert.Equal("Poznań", result.Address.Municipality);
        Assert.Equal("Poznań", result.Address.City);
        Assert.Equal("60-001", result.Address.PostalCode);
        Assert.Equal("Kwiatowa", result.Address.Street);
        Assert.Equal("12", result.Address.BuildingNumber);
        Assert.Equal("3", result.Address.ApartmentNumber);
        Assert.Equal("Poznań", result.Address.PostOfficeCity);
        Assert.All(http.Clients, client => Assert.False(client.DefaultRequestHeaders.Contains("sid")));
    }

    [Fact]
    public async Task MtomRootPartCanFollowAnotherPartAndContainCdataXml()
    {
        using var handler = new StubHandler((request, _) =>
        {
            var response = Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : EntityXml);
            var xml = response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
            response.Content.Dispose();
            var multipart = new MultipartContent("related", "test-boundary");
            multipart.Headers.ContentType!.Parameters.Add(new NameValueHeaderValue("start", "\"<soap-root>\""));
            var attachment = new StringContent("not the SOAP root");
            attachment.Headers.Add("Content-ID", "<attachment>");
            multipart.Add(attachment);
            var root = new StringContent(xml, Encoding.UTF8, "application/xop+xml");
            root.Headers.Add("Content-ID", "<soap-root>");
            root.Headers.Add("Content-Transfer-Encoding", "binary");
            multipart.Add(root);
            response.Content = multipart;
            return Task.FromResult(response);
        });
        var http = new StubHttpClientFactory(handler);
        Assert.Single(await Client(http).SearchByNipAsync("8567346215"));
    }

    [Fact]
    public async Task EscapedXmlMultipleResultsAndMissingOptionalAddressFieldsAreSupported()
    {
        var xml = "<root><dane><Regon>012345675</Regon><Nazwa>Pierwsza</Nazwa></dane><dane><Regon>192598184</Regon><Nazwa>Druga</Nazwa></dane></root>";
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : xml, cdata: false)));
        var http = new StubHttpClientFactory(handler);
        var results = await Client(http).SearchByKrsAsync("0000123456");
        Assert.Equal(2, results.Count);
        Assert.Null(results[0].Address.Street);
        Assert.Null(results[0].Nip);
    }

    [Theory]
    [InlineData("4", true)]
    [InlineData("0", true)]
    [InlineData("7", false)]
    [InlineData("", false)]
    public async Task EmptySearchResponseUsesDiagnosticNamespaceToDistinguishMissingDataFromExpiredSession(string code, bool noData)
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation switch { "Zaloguj" => Sid, "GetValue" => code, _ => "" })));
        var http = new StubHttpClientFactory(handler);
        var client = Client(http);
        if (noData) Assert.Empty(await client.SearchByNipAsync("8567346215"));
        else Assert.Equal(GusBirError.SessionExpired, (await Assert.ThrowsAsync<GusBirException>(() => client.SearchByNipAsync("8567346215"))).Error);
        var diagnostic = handler.Requests[2];
        Assert.Equal(Sid, diagnostic.Sid);
        Assert.Equal("KomunikatKod", diagnostic.Body.Descendants(XName.Get("pNazwaParametru", Diagnostic)).Single().Value);
        Assert.Equal(Diagnostic + "/IUslugaBIR/GetValue", diagnostic.Body.Descendants(XName.Get("Action", "http://www.w3.org/2005/08/addressing")).Single().Value);
    }

    [Fact]
    public async Task SimulatedNoDataSoapResponseReturnsEmptyCollectionAndAllowsLogout()
    {
        // No-data behavior is determined by the simulated SOAP response, not by an absent live identifier.
        const string noDataXml = "<root><dane><ErrorCode>4</ErrorCode><ErrorMessagePl>Nie znaleziono podmiotu dla podanych kryteriów wyszukiwania.</ErrorMessagePl><Nip>9999999999</Nip></dane></root>";
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation switch
        {
            "Zaloguj" => Sid,
            "DaneSzukajPodmioty" => noDataXml,
            "Wyloguj" => "true",
            _ => throw new InvalidOperationException("Unexpected operation in simulated no-data test.")
        })));
        var http = new StubHttpClientFactory(handler);
        var client = Client(http);
        await client.LoginAsync();
        try
        {
            Assert.Empty(await client.SearchByNipAsync("9999999999"));
        }
        finally
        {
            await client.LogoutAsync();
        }
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty", "Wyloguj" }, handler.Requests.Select(request => request.Operation));
        Assert.Equal(Sid, handler.Requests[1].Sid);
        Assert.Equal(Sid, handler.Requests[2].Sid);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(7, false)]
    [InlineData(2, false)]
    public async Task StructuredGusErrorRowsAreNotReturnedAsEntities(int code, bool noData)
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : $"<root><dane><ErrorCode>{code}</ErrorCode><ErrorMessagePl>Untrusted text</ErrorMessagePl></dane></root>")));
        var http = new StubHttpClientFactory(handler);
        var client = Client(http);
        if (noData) Assert.Empty(await client.SearchByNipAsync("8567346215"));
        else
        {
            var error = await Assert.ThrowsAsync<GusBirException>(() => client.SearchByNipAsync("8567346215"));
            Assert.Equal(code == 7 ? GusBirError.SessionExpired : GusBirError.ServiceRejected, error.Error);
            Assert.Equal(code, error.ServiceCode);
            Assert.DoesNotContain("Untrusted text", error.ToString());
        }
    }

    [Fact]
    public async Task ConcurrentCallsReuseOnlyThisClientsSessionAndLogoutUsesHeaderAndBody()
    {
        using var handler = new StubHandler(async (request, _) =>
        {
            await Task.Yield();
            return Reply(request.Operation, request.Operation switch { "Zaloguj" => Sid, "Wyloguj" => "true", _ => "<root/>" });
        });
        var http = new StubHttpClientFactory(handler);
        var client = Client(http);
        await Task.WhenAll(Enumerable.Range(0, 4).Select(_ => client.SearchByNipAsync("8567346215")));
        Assert.Single(handler.Requests, request => request.Operation == "Zaloguj");
        await client.LogoutAsync();
        var logout = handler.Requests.Last();
        Assert.Equal("Wyloguj", logout.Operation);
        Assert.Equal(Sid, logout.Sid);
        Assert.Equal(Sid, logout.Body.Descendants(XName.Get("pIdentyfikatorSesji", Service)).Single().Value);
        await client.LogoutAsync();
        Assert.Single(handler.Requests, request => request.Operation == "Wyloguj");
        await client.SearchByNipAsync("8567346215");
        Assert.Equal(2, handler.Requests.Count(request => request.Operation == "Zaloguj"));
        await Client(http).LoginAsync();
        Assert.Equal(3, handler.Requests.Count(request => request.Operation == "Zaloguj"));
    }

    [Fact]
    public async Task LocalSessionExpiryRequiresNewLogin()
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, Sid)));
        var http = new StubHttpClientFactory(handler);
        var clock = new TestClock();
        var client = new GusBirClient(http, Options.Create(new GusBirOptions()), clock, CreateScopeFactory());
        await client.LoginAsync();
        await client.LoginAsync();
        Assert.Single(handler.Requests);
        clock.UtcNow = clock.UtcNow.AddMinutes(56);
        await client.LoginAsync();
        Assert.Equal(2, handler.Requests.Count);
    }

    [Fact]
    public async Task SequentialSearchesReuseSidAcrossFreshHttpClientsWithoutLogoutOrStatusChecks()
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : EntityXml)));
        var factory = new StubHttpClientFactory(handler);
        var client = Client(factory);
        var first = Assert.Single(await client.SearchByNipAsync("8567346215"));
        var second = Assert.Single(await client.SearchByRegonAsync("012345675"));
        Assert.Equal(first, second);
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty", "DaneSzukajPodmioty" }, handler.Requests.Select(request => request.Operation));
        Assert.All(handler.Requests.Skip(1), request => Assert.Equal(Sid, request.Sid));
        Assert.Equal(3, factory.Clients.Count);
        Assert.NotSame(factory.Clients[0], factory.Clients[1]);
        Assert.NotSame(factory.Clients[1], factory.Clients[2]);
    }

    [Theory]
    [InlineData(54, false)]
    [InlineData(55, true)]
    [InlineData(56, true)]
    public async Task SearchRenewsSessionAtAbsoluteFiftyFiveMinuteBoundary(int minutes, bool renew)
    {
        var logins = 0;
        var renewedSid = new string('B', 20);
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation,
            request.Operation == "Zaloguj" ? ++logins == 1 ? Sid : renewedSid : EntityXml)));
        var clock = new TestClock();
        var client = new GusBirClient(new StubHttpClientFactory(handler), Options.Create(new GusBirOptions()), clock, CreateScopeFactory());
        await client.SearchByNipAsync("8567346215");
        clock.UtcNow = clock.UtcNow.AddMinutes(minutes);
        await client.SearchByNipAsync("8567346215");
        Assert.Equal(renew ? 2 : 1, logins);
        Assert.Equal(renew ? renewedSid : Sid, handler.Requests.Last().Sid);
        Assert.DoesNotContain(handler.Requests, request => request.Operation is "Wyloguj" or "GetValue");
    }

    [Fact]
    public async Task IntermediateSearchesDoNotExtendSessionLifetime()
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : EntityXml)));
        var clock = new TestClock();
        var client = new GusBirClient(new StubHttpClientFactory(handler), Options.Create(new GusBirOptions()), clock, CreateScopeFactory());
        await client.SearchByNipAsync("8567346215");
        clock.UtcNow = clock.UtcNow.AddMinutes(30);
        await client.SearchByNipAsync("8567346215");
        clock.UtcNow = clock.UtcNow.AddMinutes(24);
        await client.SearchByNipAsync("8567346215");
        Assert.Single(handler.Requests, request => request.Operation == "Zaloguj");
        clock.UtcNow = clock.UtcNow.AddMinutes(1);
        await client.SearchByNipAsync("8567346215");
        Assert.Equal(2, handler.Requests.Count(request => request.Operation == "Zaloguj"));
    }

    [Fact]
    public async Task DiSharesClientAndSidAcrossRequestScopesWithoutCapturingScopedServices()
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : EntityXml)));
        var services = new ServiceCollection();
        services.AddGusBirInfrastructure(new ConfigurationBuilder().Build());
        services.AddScoped<IGusRateLimiter>(_ => new StubRateLimiter());
        services.AddHttpClient(GusBirServiceCollectionExtensions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        Assert.Equal(ServiceLifetime.Singleton, Assert.Single(services, service => service.ServiceType == typeof(IGusBirClient)).Lifetime);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        IGusBirClient firstClient;
        using (var firstRequest = provider.CreateScope())
        {
            firstClient = firstRequest.ServiceProvider.GetRequiredService<IGusBirClient>();
            Assert.Single(await firstClient.SearchByNipAsync("8567346215"));
        }
        using (var secondRequest = provider.CreateScope())
        {
            var secondClient = secondRequest.ServiceProvider.GetRequiredService<IGusBirClient>();
            Assert.Same(firstClient, secondClient);
            Assert.Single(await secondClient.SearchByRegonAsync("012345675"));
        }
        Assert.Single(handler.Requests, request => request.Operation == "Zaloguj");
        Assert.All(handler.Requests.Skip(1), request => Assert.Equal(Sid, request.Sid));
        Assert.DoesNotContain(handler.Requests, request => request.Operation == "Wyloguj");
    }

    [Fact]
    public async Task ConcurrentDiRequestsShareLoginAndSerializeSearchWithDiagnostic()
    {
        using var handler = new StubHandler(async (request, _) =>
        {
            await Task.Yield();
            return Reply(request.Operation, request.Operation switch { "Zaloguj" => Sid, "GetValue" => "4", _ => "" });
        });
        var services = new ServiceCollection();
        services.AddGusBirInfrastructure(new ConfigurationBuilder().Build());
        services.AddScoped<IGusRateLimiter>(_ => new StubRateLimiter());
        services.AddHttpClient(GusBirServiceCollectionExtensions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        await Task.WhenAll(Enumerable.Range(0, 16).Select(async _ =>
        {
            using var request = provider.CreateScope();
            Assert.Empty(await request.ServiceProvider.GetRequiredService<IGusBirClient>().SearchByNipAsync("8567346215"));
        }));
        Assert.Single(handler.Requests, request => request.Operation == "Zaloguj");
        Assert.Equal(33, handler.Requests.Count);
        for (var i = 1; i < handler.Requests.Count; i += 2)
        {
            Assert.Equal("DaneSzukajPodmioty", handler.Requests[i].Operation);
            Assert.Equal("GetValue", handler.Requests[i + 1].Operation);
            Assert.Equal(Sid, handler.Requests[i].Sid);
            Assert.Equal(Sid, handler.Requests[i + 1].Sid);
        }
    }

    [Theory]
    [InlineData("", GusBirError.Authentication)]
    [InlineData("short", GusBirError.InvalidResponse)]
    [InlineData("1234567890123456789\n", GusBirError.InvalidResponse)]
    public async Task EmptyOrMalformedSessionIsRejected(string sid, GusBirError expected)
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, sid)));
        var http = new StubHttpClientFactory(handler);
        Assert.Equal(expected, (await Assert.ThrowsAsync<GusBirException>(() => Client(http).LoginAsync())).Error);
    }

    [Theory]
    [InlineData("Nip", null)]
    [InlineData("Nip", " ")]
    [InlineData("Nip", "8567346216")]
    [InlineData("Regon", "123456786")]
    [InlineData("Krs", "123")]
    public async Task InvalidSearchIdentifiersNeverReachHttp(string kind, string? identifier)
    {
        using var handler = new StubHandler((_, _) => throw new Exception("HTTP must not run"));
        var http = new StubHttpClientFactory(handler);
        await Assert.ThrowsAnyAsync<ArgumentException>(() => Search(Client(http), kind, identifier!));
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(503, GusBirError.Unavailable)]
    [InlineData(429, GusBirError.Unavailable)]
    [InlineData(502, GusBirError.Unavailable)]
    [InlineData(401, GusBirError.Authentication)]
    public async Task HttpFailuresHaveTypedErrorsWithoutRetries(int status, GusBirError expected)
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage((HttpStatusCode)status)));
        var http = new StubHttpClientFactory(handler);
        Assert.Equal(expected, (await Assert.ThrowsAsync<GusBirException>(() => Client(http).LoginAsync())).Error);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task NetworkErrorsDoNotExposeRawExceptionOrSecrets()
    {
        using var handler = new StubHandler((_, _) => throw new HttpRequestException("secret-token-and-key"));
        var http = new StubHttpClientFactory(handler);
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(http).LoginAsync());
        Assert.Equal(GusBirError.Communication, error.Error);
        Assert.DoesNotContain("secret-token-and-key", error.ToString());
    }

    [Fact]
    public async Task SoapFaultWithHttp500DoesNotExposeRemoteFaultText()
    {
        using var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
        {
            Content = new StringContent($"<s:Envelope xmlns:s=\"{Soap}\"><s:Body><s:Fault><s:Reason><s:Text>{Sid}</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>", Encoding.UTF8, "application/soap+xml")
        }));
        var http = new StubHttpClientFactory(handler);
        var error = await Assert.ThrowsAsync<GusBirException>(() => Client(http).LoginAsync());
        Assert.Equal(GusBirError.ServiceFault, error.Error);
        Assert.DoesNotContain(Sid, error.ToString());
    }

    [Theory]
    [InlineData("html")]
    [InlineData("broken-xml")]
    [InlineData("wrong-operation")]
    [InlineData("dtd")]
    [InlineData("huge")]
    [InlineData("multipart-no-boundary")]
    public async Task InvalidTransportResponsesAreRejected(string invalid)
    {
        using var handler = new StubHandler((_, _) =>
        {
            var content = invalid switch
            {
                "html" => new StringContent("<html>unavailable</html>", Encoding.UTF8, "text/html"),
                "broken-xml" => new StringContent("<s:", Encoding.UTF8, "application/soap+xml"),
                "wrong-operation" => Reply("Wyloguj", "true").Content,
                "dtd" => new StringContent("<!DOCTYPE root [<!ENTITY injected 'value'>]><root>&injected;</root>", Encoding.UTF8, "application/soap+xml"),
                "huge" => new StringContent(new string('x', 2 * 1024 * 1024 + 1), Encoding.UTF8, "application/soap+xml"),
                _ => new StringContent("bad multipart", Encoding.UTF8, "multipart/related")
            };
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        var http = new StubHttpClientFactory(handler);
        Assert.Equal(GusBirError.InvalidResponse, (await Assert.ThrowsAsync<GusBirException>(() => Client(http).LoginAsync())).Error);
    }

    [Theory]
    [InlineData("<other/>")]
    [InlineData("<root><dane><Nazwa>No identifier</Nazwa></dane></root>")]
    [InlineData("<root><dane><Regon>012345675</Regon></dane></root>")]
    [InlineData("<!DOCTYPE root [<!ENTITY injected 'value'>]><root>&injected;</root>")]
    public async Task InvalidEmbeddedSearchXmlIsRejected(string xml)
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : xml)));
        var http = new StubHttpClientFactory(handler);
        Assert.Equal(GusBirError.InvalidResponse, (await Assert.ThrowsAsync<GusBirException>(() => Client(http).SearchByNipAsync("8567346215"))).Error);
    }

    [Fact]
    public async Task ProductionConfigurationSendsConfiguredKeyOnlyToProductionEndpoint()
    {
        const string key = "unitTestOnlyKey12345";
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, Sid)));
        var http = new StubHttpClientFactory(handler);
        var client = new GusBirClient(http, Options.Create(new GusBirOptions { Environment = GusBirEnvironment.Production, ApiKey = key }), TimeProvider.System, CreateScopeFactory());
        await client.LoginAsync();
        var request = Assert.Single(handler.Requests);
        Assert.Equal(key, request.Body.Descendants(XName.Get("pKluczUzytkownika", Service)).Single().Value);
        Assert.Equal(GusBirOptions.ProductionServiceUrl, request.Body.Descendants(XName.Get("To", "http://www.w3.org/2005/08/addressing")).Single().Value);
    }

    [Fact]
    public async Task ServerExpiredSessionIsRenewedOnceAndNextSearchReusesIt()
    {
        var searches = 0;
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation,
            request.Operation == "Zaloguj" ? Sid : ++searches == 1 ? "<root><dane><ErrorCode>7</ErrorCode></dane></root>" : "<root/>")));
        var http = new StubHttpClientFactory(handler);
        var client = Client(http);
        Assert.Empty(await client.SearchByNipAsync("8567346215"));
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty", "Zaloguj", "DaneSzukajPodmioty" }, handler.Requests.Select(request => request.Operation));
        Assert.Empty(await client.SearchByNipAsync("8567346215"));
        Assert.Equal(2, handler.Requests.Count(request => request.Operation == "Zaloguj"));
    }

    [Fact]
    public async Task FailedLogoutIsReportedAndDoesNotReuseTheOldSession()
    {
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : "false")));
        var http = new StubHttpClientFactory(handler);
        var client = Client(http);
        await client.LoginAsync();
        Assert.Equal(GusBirError.ServiceRejected, (await Assert.ThrowsAsync<GusBirException>(() => client.LogoutAsync())).Error);
        await client.LoginAsync();
        Assert.Equal(2, handler.Requests.Count(request => request.Operation == "Zaloguj"));
    }

    [Fact]
    public async Task StreamingResponseSizeIsBoundedEvenWithoutContentLength()
    {
        using var handler = new StubHandler((_, _) =>
        {
            var content = new UnknownLengthContent(new byte[2 * 1024 * 1024 + 1]);
            content.Headers.ContentType = new MediaTypeHeaderValue("application/soap+xml");
            Assert.Null(content.Headers.ContentLength);
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = content });
        });
        var http = new StubHttpClientFactory(handler);
        Assert.Equal(GusBirError.InvalidResponse, (await Assert.ThrowsAsync<GusBirException>(() => Client(http).LoginAsync())).Error);
    }

    [Fact]
    public async Task RequestTimeoutAndCallerCancellationAreDistinguished()
    {
        using var handler = new StubHandler(async (_, token) => { await Task.Delay(Timeout.InfiniteTimeSpan, token); return Reply("Zaloguj", Sid); });
        var http = new StubHttpClientFactory(handler);
        var client = new GusBirClient(http, Options.Create(new GusBirOptions { TimeoutSeconds = 1 }), TimeProvider.System, CreateScopeFactory());
        Assert.Equal(GusBirError.Timeout, (await Assert.ThrowsAsync<GusBirException>(() => client.LoginAsync())).Error);
        using var cancellation = new CancellationTokenSource();
        var login = client.LoginAsync(cancellation.Token);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => login);
    }

    private GusBirClient Client(IHttpClientFactory http) => new(http, Options.Create(new GusBirOptions()), TimeProvider.System, CreateScopeFactory());
    private IServiceScopeFactory CreateScopeFactory(IGusRateLimiter? limiter = null)
    {
        var services = new ServiceCollection();
        services.AddScoped<IGusRateLimiter>(_ => limiter ?? new StubRateLimiter());
        var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        _providers.Add(provider);
        return provider.GetRequiredService<IServiceScopeFactory>();
    }
    public void Dispose() { foreach (var provider in _providers) provider.Dispose(); }

    private sealed class StubRateLimiter : IGusRateLimiter
    {
        public Task<GusCallPermit> AcquireAsync(GusRateLimitEnvironment environment, GusWaitBudget budget, CancellationToken cancellationToken = default)
        {
            budget.ThrowIfExhausted(cancellationToken);
            return Task.FromResult(new GusCallPermit(DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(250)));
        }
    }
    private static Task<IReadOnlyList<GusEntity>> Search(IGusBirClient client, string kind, string value) => kind switch { "Nip" => client.SearchByNipAsync(value), "Regon" => client.SearchByRegonAsync(value), _ => client.SearchByKrsAsync(value) };

    private static HttpResponseMessage Reply(string operation, string result, bool cdata = true)
    {
        XNamespace service = operation == "GetValue" ? Diagnostic : Service;
        var envelope = new XElement(XName.Get("Envelope", Soap), new XElement(XName.Get("Body", Soap),
            new XElement(service + (operation + "Response"), new XElement(service + (operation + "Result"), cdata ? new XCData(result) : new XText(result)))));
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(envelope.ToString(), Encoding.UTF8, "application/soap+xml") };
    }

    private sealed record CapturedRequest(string Operation, string? Sid, XDocument Body, MediaTypeHeaderValue ContentType);
    private sealed class StubHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public List<HttpClient> Clients { get; } = [];
        public HttpClient CreateClient(string name)
        {
            Assert.Equal(GusBirServiceCollectionExtensions.HttpClientName, name);
            var client = new HttpClient(handler, disposeHandler: false) { Timeout = Timeout.InfiniteTimeSpan };
            Clients.Add(client);
            return client;
        }
    }
    private sealed class StubHandler(Func<CapturedRequest, CancellationToken, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<CapturedRequest> Requests { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = XDocument.Parse(await request.Content!.ReadAsStringAsync(cancellationToken));
            var capture = new CapturedRequest(body.Root!.Element(XName.Get("Body", Soap))!.Elements().Single().Name.LocalName,
                request.Headers.TryGetValues("sid", out var sid) ? sid.Single() : null, body, request.Content.Headers.ContentType!);
            Requests.Add(capture);
            return await respond(capture, cancellationToken);
        }
    }

    private sealed class TestClock : TimeProvider
    {
        public DateTimeOffset UtcNow { get; set; } = DateTimeOffset.UtcNow;
        public override DateTimeOffset GetUtcNow() => UtcNow;
    }

    private sealed class UnknownLengthContent(byte[] bytes) : ByteArrayContent(bytes)
    {
        protected override bool TryComputeLength(out long length) { length = 0; return false; }
    }
}
