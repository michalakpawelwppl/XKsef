using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using xKsef.Api.Authorization;
using xKsef.Api.Controllers;
using xKsef.Api.ExceptionHandlers;
using xKsef.Application.Contractors;
using xKsef.Application.Gus;
using xKsef.Application.Tenants;
using xKsef.Contracts.Contractors;
using xKsef.Tests.Infrastructure;

namespace xKsef.Tests.Integration;

public sealed partial class ContractorApiTests
{
    [Theory]
    [InlineData(500, 100, true)]
    [InlineData(501, 100, false)]
    [InlineData(500, 101, false)]
    public async Task NameLimitsApplyAfterTrimming(int fullLength, int shortLength, bool valid)
    {
        await using var api = await Fixture.CreateAsync();
        var json = JsonSerializer.Serialize(new { FullName = " " + new string('x', fullLength) + " ", ShortName = " " + new string('x', shortLength) + " " });
        using var response = await api.SendAsync(HttpMethod.Post, "/api/contractors", json);
        Assert.Equal(valid ? HttpStatusCode.Created : HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"fullName\":\"Firma\"}")]
    [InlineData("{\"shortName\":\"F\"}")]
    [InlineData("{\"fullName\":\"Firma\",\"shortName\":\" \"}")]
    [InlineData("{\"fullName\":\"Firma\",\"shortName\":\"F\",\"nip\":\"8567346216\"}")]
    [InlineData("{\"fullName\":\"Firma\",\"shortName\":\"F\",\"isEuVatRegistered\":true}")]
    [InlineData("{\"fullName\":\"Firma\",\"shortName\":\"F\",\"addresses\":[null]}")]
    [InlineData("{\"fullName\":\"Firma\",\"shortName\":\"F\",\"addresses\":[{\"addressType\":2,\"city\":\"A\"}]}")]
    [InlineData("{\"fullName\":\"Firma\",\"shortName\":\"F\",\"addresses\":[{\"addressType\":1,\"city\":\"A\"},{\"addressType\":1,\"city\":\"B\"}]}")]
    public async Task PostRejectsInvalidRequestsWith400(string json)
    {
        await using var api = await Fixture.CreateAsync();
        using var response = await api.SendAsync(HttpMethod.Post, "/api/contractors", json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Empty(await api.Store.Repository.GetContractorsAsync(api.Store.TenantA));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PostAlwaysCreatesActiveAndGetReturnsCollectionWhenAddressesAreMissingOrNull(bool explicitNull)
    {
        await using var api = await Fixture.CreateAsync();
        var json = "{\"fullName\":\"Firma\",\"shortName\":\"F\",\"isActive\":false" + (explicitNull ? ",\"addresses\":null}" : "}");
        using var created = await api.SendAsync(HttpMethod.Post, "/api/contractors", json);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        Assert.NotNull(created.Headers.Location);
        using var get = await api.SendAsync(HttpMethod.Get, created.Headers.Location!.ToString());
        Assert.Equal(HttpStatusCode.OK, get.StatusCode);
        var dto = (await get.Content.ReadFromJsonAsync<ContractorDto>())!;
        Assert.True(dto.IsActive);
        Assert.Empty(dto.Addresses);
        using var body = JsonDocument.Parse(await get.Content.ReadAsStringAsync());
        Assert.Equal(JsonValueKind.Array, body.RootElement.GetProperty("addresses").ValueKind);
        Assert.False(body.RootElement.TryGetProperty("name", out _));
        Assert.False(body.RootElement.TryGetProperty("address", out _));
    }

    [Theory]
    [InlineData("{\"fullName\":\"Firma\",\"shortName\":\"F\",\"isActive\":true}")]
    [InlineData("{\"fullName\":\"Firma\",\"shortName\":\"F\",\"isActive\":true,\"addresses\":null}")]
    [InlineData("{\"fullName\":\"Firma\",\"shortName\":\"F\",\"addresses\":[]}")]
    [InlineData("{\"fullName\":\"Firma\",\"shortName\":\"F\",\"isActive\":null,\"addresses\":[]}")]
    public async Task PutRequiresExplicitActivationAndNonNullAddressCollection(string json)
    {
        await using var api = await Fixture.CreateAsync();
        var id = await api.Store.Service.CreateAsync(api.Store.TenantA, ContractorTestStore.Command(true));
        using var response = await api.SendAsync(HttpMethod.Put, $"/api/contractors/{id}", json);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single((await api.Store.Repository.GetByIdAsync(api.Store.TenantA, id))!.Addresses);
    }

    [Fact]
    public async Task HttpRoundTripMapsEveryFieldAndSupportsUpdateDeactivationAndEmptyCollection()
    {
        await using var api = await Fixture.CreateAsync();
        var payload = new
        {
            FullName = " Pełna nazwa ", ShortName = " AKR ", Nip = "8567346215", Regon = " 12345678512347 ", KrsNumber = " 0000123456 ",
            EuVatNumber = "PL8567346215", OtherTaxNumber = "OTHER", IsEuVatRegistered = true,
            Addresses = new[] { new { AddressType = 1, CountryCode = "PL", City = "Poznań", Street = "Kwiatowa", BuildingNumber = "12", ApartmentNumber = "3", PostalCode = "60-001" } }
        };
        using var created = await api.SendAsync(HttpMethod.Post, "/api/contractors", JsonSerializer.Serialize(payload));
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var location = created.Headers.Location!.ToString();
        using var get = await api.SendAsync(HttpMethod.Get, location);
        var dto = (await get.Content.ReadFromJsonAsync<ContractorDto>())!;
        Assert.Equal("Pełna nazwa", dto.FullName);
        Assert.Equal("AKR", dto.ShortName);
        Assert.Equal(payload.Nip, dto.Nip);
        Assert.Equal(payload.Regon.Trim(), dto.Regon);
        Assert.Equal(payload.KrsNumber.Trim(), dto.KrsNumber);
        Assert.Equal(payload.EuVatNumber, dto.EuVatNumber);
        Assert.Equal(payload.OtherTaxNumber, dto.OtherTaxNumber);
        Assert.True(dto.IsEuVatRegistered);
        var address = Assert.Single(dto.Addresses);
        Assert.Equal(1, address.AddressType);
        Assert.Equal("Kwiatowa", address.Street);
        Assert.Equal("12", address.BuildingNumber);
        Assert.Equal("3", address.ApartmentNumber);
        Assert.Equal("60-001", address.PostalCode);
        using var updated = await api.SendAsync(HttpMethod.Put, location, "{\"fullName\":\"Zmieniona\",\"shortName\":\"Z\",\"isActive\":false,\"addresses\":[]}");
        Assert.Equal(HttpStatusCode.NoContent, updated.StatusCode);
        using var list = await api.SendAsync(HttpMethod.Get, "/api/contractors");
        var inactive = Assert.Single((await list.Content.ReadFromJsonAsync<List<ContractorDto>>())!);
        Assert.False(inactive.IsActive);
        Assert.Empty(inactive.Addresses);
        Assert.Equal(dto.Id, inactive.Id);
        Assert.Equal(dto.CreatedAt, inactive.CreatedAt);
    }

    [Fact]
    public async Task AuthorizationAndTenantFilterPreventCrossTenantAccess()
    {
        await using var api = await Fixture.CreateAsync();
        var id = await api.Store.Service.CreateAsync(api.Store.TenantB, ContractorTestStore.Command(true));
        using var foreign = await api.SendAsync(HttpMethod.Get, $"/api/contractors/{id}");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        using var foreignPut = await api.SendAsync(HttpMethod.Put, $"/api/contractors/{id}", "{\"fullName\":\"X\",\"shortName\":\"X\",\"isActive\":false,\"addresses\":[]}");
        Assert.Equal(HttpStatusCode.NotFound, foreignPut.StatusCode);
        using var denied = await api.SendAsync(HttpMethod.Get, "/api/contractors", tenant: api.Store.TenantB.ToString());
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var invalidHeader = await api.SendAsync(HttpMethod.Get, "/api/contractors", tenant: "invalid");
        Assert.Equal(HttpStatusCode.BadRequest, invalidHeader.StatusCode);
        api.Client.DefaultRequestHeaders.Authorization = null;
        using var anonymous = await api.SendAsync(HttpMethod.Get, "/api/contractors");
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
    }

    private sealed class Fixture : IAsyncDisposable
    {
        public ContractorTestStore Store { get; private set; } = null!;
        public IHost Host { get; private set; } = null!;
        public HttpClient Client { get; private set; } = null!;
        public StubGusClient Gus { get; } = new();

        public static async Task<Fixture> CreateAsync()
        {
            var fixture = new Fixture { Store = await ContractorTestStore.CreateAsync() };
            fixture.Host = await new HostBuilder().ConfigureWebHost(web => web.UseTestServer().ConfigureLogging(log => log.ClearProviders()).ConfigureServices(services =>
            {
                services.AddSingleton<IContractorService>(fixture.Store.Service);
                services.AddSingleton<IGusBirClient>(fixture.Gus);
                services.AddExceptionHandler<GusExceptionHandler>();
                services.AddProblemDetails();
                services.AddSingleton<ITenantAccessService, TestTenantAccess>();
                services.AddScoped<TenantAuthorizationFilter>();
                services.AddControllers().AddApplicationPart(typeof(ContractorsController).Assembly);
                services.AddAuthentication("test").AddScheme<AuthenticationSchemeOptions, TestAuthenticationHandler>("test", _ => { });
                services.AddAuthorization();
            }).Configure(app =>
            {
                app.UseExceptionHandler(); app.UseRouting(); app.UseAuthentication(); app.UseAuthorization(); app.UseEndpoints(endpoints => endpoints.MapControllers());
            })).StartAsync();
            fixture.Client = fixture.Host.GetTestClient();
            fixture.Client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("test", fixture.Store.TenantA.ToString());
            return fixture;
        }

        public async Task<HttpResponseMessage> SendAsync(HttpMethod method, string path, string? json = null, string? tenant = null)
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.Add("X-Tenant-Id", tenant ?? Store.TenantA.ToString());
            if (json is not null)
                request.Content = new StringContent(json, Encoding.UTF8, "application/json");
            return await Client.SendAsync(request);
        }

        public async ValueTask DisposeAsync()
        {
            Client.Dispose();
            Host.Dispose();
            await Store.DisposeAsync();
        }
    }

    private sealed class TestTenantAccess : ITenantAccessService
    {
        public Task<bool> HasAccessAsync(string userId, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult(userId == tenantId.ToString());
        public Task<IReadOnlyList<string>> GetRolesAsync(string userId, Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<string>>([]);
    }

    private sealed class TestAuthenticationHandler(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var authorization = Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("test ", StringComparison.Ordinal))
                return Task.FromResult(AuthenticateResult.NoResult());
            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, authorization[5..])], Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
