using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using xKsef.Api.ExceptionHandlers;
using xKsef.Application.Gus;
using xKsef.Tests.Infrastructure;

namespace xKsef.Tests.Integration;

public sealed partial class ContractorApiTests
{
    [Theory]
    [InlineData("nip", "5261040828")]
    [InlineData("regon", "000331501")]
    [InlineData("regon", "12345678512347")]
    [InlineData("krs", "0000123456")]
    public async Task GusLookupUsesExactlyOneNormalizedIdentifierAndKeepsLeadingZeros(string kind, string value)
    {
        await using var api = await Fixture.CreateAsync();
        api.Gus.Entities = [GusEntityExample()];
        using var response = await api.SendAsync(HttpMethod.Get, "/api/contractors/gus?" + kind + "=" + Uri.EscapeDataString(" " + value + " "));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal((kind, value), Assert.Single(api.Gus.Calls));
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(1, json.RootElement.GetArrayLength());
        Assert.True(response.Headers.CacheControl!.NoStore);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?nip=")]
    [InlineData("?regon=%20%20")]
    [InlineData("?krs=")]
    [InlineData("?nip=5261040828&regon=000331501")]
    [InlineData("?nip=5261040828&krs=0000123456")]
    [InlineData("?regon=000331501&krs=0000123456")]
    [InlineData("?nip=5261040828&regon=000331501&krs=0000123456")]
    [InlineData("?nip=5261040828&regon=")]
    [InlineData("?nip=5261040828&nip=5261040828")]
    [InlineData("?nip=5261040829")]
    [InlineData("?nip=526104082")]
    [InlineData("?nip=５２６１０４０８２８")]
    [InlineData("?regon=000331502")]
    [InlineData("?regon=12345678512348")]
    [InlineData("?krs=123")]
    [InlineData("?krs=000012345a")]
    public async Task GusLookupRejectsInvalidOrAmbiguousCriteriaBeforeCallingGus(string query)
    {
        await using var api = await Fixture.CreateAsync();
        using var response = await api.SendAsync(HttpMethod.Get, "/api/contractors/gus" + query);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(api.Gus.Calls);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    public async Task GusLookupReturnsAllResultsInOriginalOrderAndNeverSavesContractors(int count)
    {
        await using var api = await Fixture.CreateAsync();
        var id = await api.Store.Service.CreateAsync(api.Store.TenantA, ContractorTestStore.Command(true));
        var before = (await api.Store.Repository.GetByIdAsync(api.Store.TenantA, id))!;
        api.Gus.Entities = Enumerable.Range(0, count).Select(index => GusEntityExample() with { Regon = $"source-{index}", FullName = "Wynik " + index }).ToArray();
        api.Store.Sql.Reads = 0;
        using var response = await api.SendAsync(HttpMethod.Get, "/api/contractors/gus?nip=5261040828");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(0, api.Store.Sql.Reads); // Endpoint never accesses the contractor repository.
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, json.RootElement.ValueKind);
        Assert.Equal(api.Gus.Entities.Select(entity => entity.Regon), json.RootElement.EnumerateArray().Select(entity => entity.GetProperty("regon").GetString()));
        Assert.Equal(1, await api.Store.Context.Contractors.CountAsync());
        Assert.Equal(1, await api.Store.Context.ContractorsData.CountAsync());
        Assert.Equal(1, await api.Store.Context.Addresses.CountAsync());
        Assert.Equal(1, await api.Store.Context.AddressesData.CountAsync());
        var after = (await api.Store.Repository.GetByIdAsync(api.Store.TenantA, id))!;
        Assert.Equal(before.Contractor.CreatedAt, after.Contractor.CreatedAt);
        Assert.Equal(before.ContractorData.FullName, after.ContractorData.FullName);
        Assert.Equal(before.Addresses[0].AddressData.City, after.Addresses[0].AddressData.City);
    }

    [Fact]
    public async Task GusLookupMapsApiContractAndPreservesMissingFieldsInsteadOfInferringThem()
    {
        await using var api = await Fixture.CreateAsync();
        api.Gus.Entities = [GusEntityExample(), new GusEntity { Regon = "000331501", FullName = "Tylko nazwa i REGON" }];
        using var response = await api.SendAsync(HttpMethod.Get, "/api/contractors/gus?krs=0000123456");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        var full = json.RootElement[0];
        Assert.Equal(new[] { "address", "fullName", "krs", "nip", "regon", "shortName" }, full.EnumerateObject().Select(p => p.Name).Order());
        Assert.Equal("5261040828", full.GetProperty("nip").GetString());
        Assert.Equal("000331501", full.GetProperty("regon").GetString());
        Assert.Equal("GŁÓWNY URZĄD STATYSTYCZNY", full.GetProperty("fullName").GetString());
        Assert.Equal("0000123456", full.GetProperty("krs").GetString());
        Assert.Equal(JsonValueKind.Null, full.GetProperty("shortName").ValueKind);
        var expectedAddress = JsonSerializer.SerializeToElement(api.Gus.Entities[0].Address, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        Assert.True(JsonElement.DeepEquals(expectedAddress, full.GetProperty("address")));
        var missing = json.RootElement[1];
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("nip").ValueKind);
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("krs").ValueKind); // Do not copy KRS from the query.
        Assert.Equal(JsonValueKind.Null, missing.GetProperty("shortName").ValueKind);
        Assert.All(missing.GetProperty("address").EnumerateObject(), property => Assert.Equal(JsonValueKind.Null, property.Value.ValueKind));
    }

    [Fact]
    public async Task GusLookupDoesNotCacheResultsBetweenRequests()
    {
        await using var api = await Fixture.CreateAsync();
        api.Gus.Entities = [GusEntityExample()];
        using var first = await api.SendAsync(HttpMethod.Get, "/api/contractors/gus?regon=000331501");
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        api.Gus.Entities = [];
        using var second = await api.SendAsync(HttpMethod.Get, "/api/contractors/gus?regon=000331501");
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.Equal("[]", await second.Content.ReadAsStringAsync());
        Assert.Equal(2, api.Gus.Calls.Count);
    }

    [Theory]
    [InlineData("limit", 429)]
    [InlineData("sql", 503)]
    [InlineData("timeout", 504)]
    [InlineData("communication", 502)]
    [InlineData("unavailable", 502)]
    [InlineData("authentication", 502)]
    [InlineData("session", 502)]
    [InlineData("invalid-response", 502)]
    [InlineData("fault", 502)]
    [InlineData("rejected", 502)]
    public async Task GusLookupMapsTypedErrorsToSanitizedProblemDetails(string kind, int status)
    {
        await using var api = await Fixture.CreateAsync();
        api.Gus.Error = kind switch
        {
            "limit" => new GusRateLimitExceededException(TimeSpan.FromMilliseconds(1501)),
            "sql" => new GusRateLimiterUnavailableException(new IOException("secret-sid secret-key SOAP sql-server-credentials")),
            _ => new GusBirException(kind switch
            {
                "timeout" => GusBirError.Timeout, "communication" => GusBirError.Communication,
                "unavailable" => GusBirError.Unavailable, "authentication" => GusBirError.Authentication,
                "session" => GusBirError.SessionExpired, "invalid-response" => GusBirError.InvalidResponse,
                "fault" => GusBirError.ServiceFault, _ => GusBirError.ServiceRejected
            }, "secret-sid secret-key SOAP sql-server-credentials", 7)
        };
        using var response = await api.SendAsync(HttpMethod.Get, "/api/contractors/gus?nip=5261040828");
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var body = await response.Content.ReadAsStringAsync();
        using var json = JsonDocument.Parse(body);
        Assert.Equal(status, json.RootElement.GetProperty("status").GetInt32());
        foreach (var secret in new[] { "secret-sid", "secret-key", "SOAP", "sql-server-credentials" }) Assert.DoesNotContain(secret, body);
        if (status == 429) Assert.Equal("2", Assert.Single(response.Headers.GetValues("Retry-After")));
        else Assert.False(response.Headers.Contains("Retry-After"));
        Assert.Single(api.Gus.Calls);
    }

    [Theory]
    [InlineData(null, "1")]
    [InlineData(0, "1")]
    [InlineData(1, "1")]
    [InlineData(1000, "1")]
    [InlineData(1001, "2")]
    public async Task GusLookupRoundsRetryAfterUpAndProvidesFallback(int? milliseconds, string expected)
    {
        await using var api = await Fixture.CreateAsync();
        api.Gus.Error = new GusRateLimitExceededException(milliseconds is { } ms ? TimeSpan.FromMilliseconds(ms) : null);
        using var response = await api.SendAsync(HttpMethod.Get, "/api/contractors/gus?regon=000331501");
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal(expected, Assert.Single(response.Headers.GetValues("Retry-After")));
    }

    [Fact]
    public async Task GusLookupForwardsRequestAbortedAndDoesNotConvertCancellationToGusFailure()
    {
        await using var api = await Fixture.CreateAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var canceled = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        api.Gus.OnSearch = async token =>
        {
            Assert.True(token.CanBeCanceled);
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { canceled.TrySetResult(); }
            return [];
        };
        using var caller = new CancellationTokenSource();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/contractors/gus?nip=5261040828");
        request.Headers.Add("X-Tenant-Id", api.Store.TenantA.ToString());
        var pending = api.Client.SendAsync(request, caller.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await canceled.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Single(api.Gus.Calls);
        var context = new DefaultHttpContext();
        Assert.False(await new GusExceptionHandler().TryHandleAsync(context, new OperationCanceledException(), CancellationToken.None));
    }

    private static GusEntity GusEntityExample() => new()
    {
        Nip = "5261040828", Regon = "000331501", KrsNumber = "0000123456", FullName = "GŁÓWNY URZĄD STATYSTYCZNY",
        Address = new GusAddress
        {
            Voivodeship = "MAZOWIECKIE", County = "m. st. Warszawa", Municipality = "Śródmieście", City = "Warszawa",
            PostalCode = "00-925", Street = "ul. Test-Krucza", BuildingNumber = "208", ApartmentNumber = null, PostOfficeCity = "Warszawa"
        }
    };

    private sealed class StubGusClient : IGusBirClient
    {
        public IReadOnlyList<GusEntity> Entities { get; set; } = [];
        public Exception? Error { get; set; }
        public Func<CancellationToken, Task<IReadOnlyList<GusEntity>>>? OnSearch { get; set; }
        public List<(string Kind, string Value)> Calls { get; } = [];
        public Task LoginAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Endpoint must not manage SID");
        public Task LogoutAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Endpoint must not manage SID");
        public Task<IReadOnlyList<GusEntity>> SearchByNipAsync(string nip, CancellationToken cancellationToken = default) => SearchAsync("nip", nip, cancellationToken);
        public Task<IReadOnlyList<GusEntity>> SearchByRegonAsync(string regon, CancellationToken cancellationToken = default) => SearchAsync("regon", regon, cancellationToken);
        public Task<IReadOnlyList<GusEntity>> SearchByKrsAsync(string krsNumber, CancellationToken cancellationToken = default) => SearchAsync("krs", krsNumber, cancellationToken);
        private Task<IReadOnlyList<GusEntity>> SearchAsync(string kind, string value, CancellationToken token)
        {
            Calls.Add((kind, value));
            token.ThrowIfCancellationRequested();
            if (Error is not null) return Task.FromException<IReadOnlyList<GusEntity>>(Error);
            return OnSearch is not null ? OnSearch(token) : Task.FromResult(Entities);
        }
    }
}
