using System.Net;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using xKesf.Application.Contractors;
using xKsef.Application.Contractors;
using xKsef.Application.Gus;
using xKsef.Infrastructure.Persistence;
using xKsef.Tests.Infrastructure;

namespace xKsef.Tests.Integration;

// Uses the existing fixture's production AddApiAuthentication, JWT parameters and TenantAccessService.
// Its collection owns an isolated LocalDB database; only the external GUS dependency is replaced.
public sealed partial class BrowserAuthenticationTests
{
    [Theory]
    [InlineData("none")]
    [InlineData("malformed")]
    [InlineData("expired")]
    [InlineData("signature")]
    [InlineData("issuer")]
    [InlineData("audience")]
    [InlineData("unsigned")]
    [InlineData("hs512")]
    [InlineData("missing-exp")]
    [InlineData("future-nbf")]
    public async Task GusSecurityRejectsUnauthenticatedAndInvalidJwtWithoutCallingGus(string kind)
    {
        await using var f = await Fixture.CreateAsync();
        var tenant = await f.CreateTenantAsync();
        var authorization = kind == "none" ? null : "Bearer " + CreateTestJwt(f, ProductionJwtParameters(f), kind);
        using var response = await f.Browser().SendAsync(HttpMethod.Get, GusPath, authorization: authorization, tenantId: tenant);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Null(response.Headers.Location);
        Assert.Empty(f.Gus.Calls);
    }

    [Fact]
    public async Task GusSecurityInvalidBearerDoesNotFallBackToValidBrowserCookie()
    {
        await using var f = await Fixture.CreateAsync();
        var browser = f.Browser();
        using var login = await browser.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var tenant = await f.CreateTenantAsync();
        using var response = await browser.SendAsync(HttpMethod.Get, GusPath, authorization: "Bearer not-a-jwt", tenantId: tenant);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Empty(f.Gus.Calls);
    }

    [Theory]
    [InlineData("missing", 400)]
    [InlineData("malformed", 400)]
    [InlineData("empty", 400)]
    [InlineData("unknown", 403)]
    [InlineData("unassigned", 403)]
    [InlineData("inactive", 403)]
    [InlineData("active", 200)]
    public async Task GusSecurityUsesRealTenantMembershipAndActiveStatus(string kind, int expected)
    {
        await using var f = await Fixture.CreateAsync();
        var tenant = kind == "unknown" ? Guid.NewGuid() : await f.CreateTenantAsync(kind == "unassigned" ? f.OtherId : f.UserId);
        if (kind == "inactive")
        {
            using var scope = f.Scope();
            await scope.ServiceProvider.GetRequiredService<AppDbContext>().Tenants.Where(t => t.Id == tenant).ExecuteUpdateAsync(set => set.SetProperty(t => t.IsActive, false));
        }
        using var request = new HttpRequestMessage(HttpMethod.Get, GusPath);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + GusJwt(f));
        if (kind != "missing") request.Headers.TryAddWithoutValidation("X-Tenant-Id", kind == "malformed" ? "not-a-guid" : kind == "empty" ? Guid.Empty.ToString() : tenant.ToString());
        using var response = await f.Client.SendAsync(request);
        Assert.Equal((HttpStatusCode)expected, response.StatusCode);
        if (expected == 200) Assert.Equal(tenant, Assert.Single(f.Gus.Calls).TenantId);
        else Assert.Empty(f.Gus.Calls);
    }

    [Fact]
    public async Task GusSecurityCanSwitchBetweenAssignedTenantsWithoutCachingResults()
    {
        await using var f = await Fixture.CreateAsync();
        var one = await f.CreateTenantAsync();
        var two = await f.CreateTenantAsync();
        var jwt = "Bearer " + GusJwt(f);
        foreach (var tenant in new[] { one, two, one })
        {
            f.Gus.Entities = [SecurityEntity(tenant.ToString())];
            using var response = await f.Browser().SendAsync(HttpMethod.Get, GusPath, authorization: jwt, tenantId: tenant);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(tenant.ToString(), json.RootElement[0].GetProperty("fullName").GetString());
            Assert.True(response.Headers.CacheControl!.NoStore);
        }
        Assert.Equal(new[] { one, two, one }, f.Gus.Calls.Select(call => call.TenantId));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task GusSecurityRechecksMembershipAndActivityOnEachRequest(bool deactivate)
    {
        await using var f = await Fixture.CreateAsync();
        var tenant = await f.CreateTenantAsync();
        var jwt = "Bearer " + GusJwt(f);
        using var first = await f.Browser().SendAsync(HttpMethod.Get, GusPath, authorization: jwt, tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            if (deactivate) await db.Tenants.Where(t => t.Id == tenant).ExecuteUpdateAsync(set => set.SetProperty(t => t.IsActive, false));
            else await db.UserTenants.Where(t => t.TenantId == tenant && t.UserId == f.UserId).ExecuteDeleteAsync();
        }
        using var second = await f.Browser().SendAsync(HttpMethod.Get, GusPath, authorization: jwt, tenantId: tenant);
        Assert.Equal(HttpStatusCode.Forbidden, second.StatusCode);
        Assert.Single(f.Gus.Calls);
    }

    [Fact]
    public async Task GusSecurityTenantQueryAndBodyCannotReplaceTheAuthorizedHeader()
    {
        await using var f = await Fixture.CreateAsync();
        var allowed = await f.CreateTenantAsync();
        var foreign = await f.CreateTenantAsync(f.OtherId);
        var jwt = "Bearer " + GusJwt(f);
        using var noHeader = await f.Browser().SendAsync(HttpMethod.Get, GusPath + "&tenantId=" + allowed,
            new { TenantId = allowed }, authorization: jwt);
        Assert.Equal(HttpStatusCode.BadRequest, noHeader.StatusCode);
        using var denied = await f.Browser().SendAsync(HttpMethod.Get, GusPath + "&tenantId=" + allowed,
            new { TenantId = allowed }, authorization: jwt, tenantId: foreign);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        Assert.Empty(f.Gus.Calls);
        using var accepted = await f.Browser().SendAsync(HttpMethod.Get, GusPath + "&tenantId=" + foreign,
            new { TenantId = foreign }, authorization: jwt, tenantId: allowed);
        Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        Assert.Equal(allowed, Assert.Single(f.Gus.Calls).TenantId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("?nip=5261040828&regon=000331501")]
    [InlineData("?nip=5261040829")]
    [InlineData("?regon=000331502")]
    [InlineData("?krs=123")]
    [InlineData("?nip=")]
    [InlineData("?regon=%20%09%20")]
    [InlineData("?krs=%20%20")]
    [InlineData("?nip=526104082%3C")]
    [InlineData("?regon=00033150%00")]
    [InlineData("?krs=000012345%27")]
    [InlineData("?nip=long")]
    [InlineData("?regon=long")]
    [InlineData("?krs=long")]
    [InlineData("?nip=5261040828&NIP=5261040828")]
    public async Task GusSecurityInvalidIdentifiersNeverReachGus(string query)
    {
        await using var f = await Fixture.CreateAsync();
        var tenant = await f.CreateTenantAsync();
        query = query.Replace("long", new string('1', 600));
        using var response = await f.Browser().SendAsync(HttpMethod.Get, "/api/contractors/gus" + query, authorization: "Bearer " + GusJwt(f), tenantId: tenant);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(f.Gus.Calls);
    }

    [Theory]
    [InlineData("nip", "0123456789")]
    [InlineData("regon", "000331501")]
    [InlineData("krs", "0000123456")]
    public async Task GusSecurityValidIdentifiersKeepLeadingZeros(string kind, string value)
    {
        await using var f = await Fixture.CreateAsync();
        var tenant = await f.CreateTenantAsync();
        using var response = await f.Browser().SendAsync(HttpMethod.Get, "/api/contractors/gus?" + kind + "=" + value, authorization: "Bearer " + GusJwt(f), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var call = Assert.Single(f.Gus.Calls);
        Assert.Equal(kind, call.Kind);
        Assert.Equal(value, call.Identifier);
    }

    [Theory]
    [InlineData("limit", 429)]
    [InlineData("sql", 503)]
    [InlineData("communication", 502)]
    [InlineData("fault", 502)]
    [InlineData("timeout", 504)]
    public async Task GusSecurityErrorResponsesNeverExposeSecrets(string kind, int status)
    {
        await using var f = await Fixture.CreateAsync();
        var tenant = await f.CreateTenantAsync();
        const string sid = "testSession123456789";
        const string key = "fakeGusApiKey12345678";
        const string soap = "<SOAP-Envelope>secret-payload</SOAP-Envelope>";
        const string connection = "Server=secret-sql-server;Database=secret-db;User Id=secret-user;Password=secret-password";
        const string internalSql = "SqlException 18456 internal-database-stack";
        var secrets = string.Join(" ", sid, key, soap, connection, internalSql);
        f.Gus.Error = kind switch
        {
            "limit" => new GusRateLimitExceededException(TimeSpan.FromMilliseconds(2001)),
            "sql" => new GusRateLimiterUnavailableException(new IOException(secrets)),
            _ => new GusBirException(kind == "timeout" ? GusBirError.Timeout : kind == "fault" ? GusBirError.ServiceFault : GusBirError.Communication, secrets)
        };
        using var response = await f.Browser().SendAsync(HttpMethod.Get, GusPath, authorization: "Bearer " + GusJwt(f), tenantId: tenant);
        Assert.Equal((HttpStatusCode)status, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType!.MediaType);
        var payload = await response.Content.ReadAsStringAsync();
        foreach (var secret in new[] { sid, key, soap, connection, "secret-sql-server", "secret-db", "secret-user", "secret-password", internalSql })
            Assert.DoesNotContain(secret, payload);
        if (status == 429) Assert.Equal("3", Assert.Single(response.Headers.GetValues("Retry-After")));
        else Assert.False(response.Headers.Contains("Retry-After"));
        Assert.Single(f.Gus.Calls);
    }

    [Fact]
    public async Task GusSecurityRequestAbortedCancelsExactlyOneGusCallWithoutGatewayError()
    {
        await using var f = await Fixture.CreateAsync();
        var tenant = await f.CreateTenantAsync();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        f.Gus.OnSearch = async token =>
        {
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, token); }
            finally { stopped.TrySetResult(); }
            return [];
        };
        using var request = new HttpRequestMessage(HttpMethod.Get, GusPath);
        request.Headers.TryAddWithoutValidation("Authorization", "Bearer " + GusJwt(f));
        request.Headers.Add("X-Tenant-Id", tenant.ToString());
        using var caller = new CancellationTokenSource();
        var pending = f.Client.SendAsync(request, caller.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10));
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.Single(f.Gus.Calls); // Spy also asserts equality with the actual HttpContext.RequestAborted.
    }

    [Fact]
    public async Task GusSecurityReturnsAllResultsWithoutChangingContractorsOrActivation()
    {
        await using var f = await Fixture.CreateAsync();
        var one = await f.CreateTenantAsync();
        var two = await f.CreateTenantAsync();
        using (var scope = f.Scope())
        {
            var service = scope.ServiceProvider.GetRequiredService<IContractorService>();
            await service.CreateAsync(one, ContractorTestStore.Command(true));
            var inactive = await service.CreateAsync(two, ContractorTestStore.Command(true));
            await service.UpdateAsync(two, inactive, new UpdateContractorCommand { FullName = "Nieaktywny", ShortName = "N", IsActive = false, Addresses = [] });
        }
        var before = await SnapshotGusContractorsAsync(f, [one, two]);
        var jwt = "Bearer " + GusJwt(f);
        f.Gus.Entities = [SecurityEntity("Trzeci"), SecurityEntity("Pierwszy"), SecurityEntity("Drugi")];
        foreach (var tenant in new[] { one, two })
        {
            using var response = await f.Browser().SendAsync(HttpMethod.Get, GusPath, authorization: jwt, tenantId: tenant);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(new[] { "Trzeci", "Pierwszy", "Drugi" }, json.RootElement.EnumerateArray().Select(row => row.GetProperty("fullName").GetString()));
        }
        f.Gus.Entities = [];
        using var empty = await f.Browser().SendAsync(HttpMethod.Get, GusPath, authorization: jwt, tenantId: one);
        Assert.Equal("[]", await empty.Content.ReadAsStringAsync());
        f.Gus.Error = new GusRateLimiterUnavailableException();
        using var failed = await f.Browser().SendAsync(HttpMethod.Get, GusPath, authorization: jwt, tenantId: one);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        Assert.Equal(before, await SnapshotGusContractorsAsync(f, [one, two]));
        Assert.Equal(4, f.Gus.Calls.Count);
    }

    private const string GusPath = "/api/contractors/gus?nip=5261040828";
    private static string GusJwt(Fixture fixture) => CreateTestJwt(fixture, ProductionJwtParameters(fixture), "valid");
    private static GusEntity SecurityEntity(string name) => new() { Regon = "000331501", FullName = name };

    private static async Task<string> SnapshotGusContractorsAsync(Fixture fixture, Guid[] tenants)
    {
        using var scope = fixture.Scope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var contractors = db.Contractors.AsNoTracking().Where(row => tenants.Contains(row.TenantId));
        var data = db.ContractorsData.AsNoTracking().Where(row => contractors.Any(c => c.Id == row.ContractorId));
        var addresses = db.Addresses.AsNoTracking().Where(row => data.Any(d => d.Id == row.ContractorsDataId));
        return JsonSerializer.Serialize(new
        {
            Contractors = await contractors.OrderBy(row => row.Id).ToArrayAsync(),
            Data = await data.OrderBy(row => row.Id).ToArrayAsync(),
            Addresses = await addresses.OrderBy(row => row.Id).ToArrayAsync(),
            AddressData = await db.AddressesData.AsNoTracking().Where(row => addresses.Any(a => a.Id == row.AddressId)).OrderBy(row => row.Id).ToArrayAsync()
        });
    }

    private sealed class SecurityGusClient : IGusBirClient
    {
        public IHttpContextAccessor ContextAccessor { get; set; } = null!;
        public IReadOnlyList<GusEntity> Entities { get; set; } = [];
        public Exception? Error { get; set; }
        public Func<CancellationToken, Task<IReadOnlyList<GusEntity>>>? OnSearch { get; set; }
        public List<(string Kind, string Identifier, Guid TenantId)> Calls { get; } = [];
        public Task LoginAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected login");
        public Task LogoutAsync(CancellationToken cancellationToken = default) => throw new InvalidOperationException("Unexpected logout");
        public Task<IReadOnlyList<GusEntity>> SearchByNipAsync(string nip, CancellationToken cancellationToken = default) => Search("nip", nip, cancellationToken);
        public Task<IReadOnlyList<GusEntity>> SearchByRegonAsync(string regon, CancellationToken cancellationToken = default) => Search("regon", regon, cancellationToken);
        public Task<IReadOnlyList<GusEntity>> SearchByKrsAsync(string krsNumber, CancellationToken cancellationToken = default) => Search("krs", krsNumber, cancellationToken);
        private Task<IReadOnlyList<GusEntity>> Search(string kind, string identifier, CancellationToken token)
        {
            var context = ContextAccessor.HttpContext!;
            Assert.Equal(context.RequestAborted, token);
            Calls.Add((kind, identifier, Assert.IsType<Guid>(context.Items["TenantId"])));
            token.ThrowIfCancellationRequested();
            if (Error is not null) return Task.FromException<IReadOnlyList<GusEntity>>(Error);
            return OnSearch is not null ? OnSearch(token) : Task.FromResult(Entities);
        }
    }
}
