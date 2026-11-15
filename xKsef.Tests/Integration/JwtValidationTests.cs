using System.IdentityModel.Tokens.Jwt;
using System.Net;
using System.Reflection;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    public static TheoryData<string, bool> InvalidJwtCases
    {
        get
        {
            var cases = new TheoryData<string, bool>();
            foreach (string kind in new[] { "expired", "future-nbf", "issuer", "audience", "signature", "missing-exp", "hs512", "unsigned", "malformed", "malformed-payload" })
            foreach (bool withCookie in new[] { false, true }) cases.Add(kind, withCookie);
            return cases;
        }
    }

    [Theory]
    [MemberData(nameof(InvalidJwtCases))]
    public async Task JwtValidationRejectsInvalidBearerOnProtectedEndpointsEvenWithValidCookie(string kind, bool withCookie)
    {
        await using var f = await Fixture.CreateAsync();
        var data = await SeedCookieInvoicesAsync(f);
        var browser = f.Browser();
        if (withCookie)
        {
            using var login = await browser.LoginAsync(); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
            // Establish that the same cookie can read the real endpoint before adding the invalid Bearer.
            await AssertCookieInvoiceQueryAsync(f, browser, data.TenantA, "Sales", "IssueDate", data.Rows, f.Prefix + "-B");
        }
        var parameters = ProductionJwtParameters(f);
        string token = CreateTestJwt(f, parameters, kind);
        foreach (string path in new[] { "/test/browser/jwt-identity", "/api/tenants", CookieInvoiceQueryPath("Sales", "IssueDate") })
        {
            f.InvoiceSql.Commands.Clear();
            using var denied = await browser.SendAsync(HttpMethod.Get, path, authorization: "Bearer " + token, tenantId: data.TenantA);
            await AssertCookieInvoiceDeniedAsync(f, denied, HttpStatusCode.Unauthorized, data.Rows);
            Assert.Contains(denied.Headers.WwwAuthenticate, header => header.Scheme == "Bearer");
            string body = await denied.Content.ReadAsStringAsync();
            Assert.DoesNotContain(token, body); Assert.DoesNotContain(f.UserId, body); Assert.DoesNotContain(f.Prefix, body);
        }
        if (withCookie)
        {
            using var me = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me");
            Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        }
    }

    [Fact]
    public async Task JwtValidationUsesProductionRulesAndSelectsOnlyTheBearerIdentity()
    {
        await using var f = await Fixture.CreateAsync();
        var parameters = ProductionJwtParameters(f);
        Assert.True(parameters.ValidateIssuer); Assert.True(parameters.ValidateAudience);
        Assert.True(parameters.ValidateIssuerSigningKey); Assert.True(parameters.ValidateLifetime);
        Assert.True(parameters.RequireSignedTokens); Assert.True(parameters.RequireExpirationTime);
        Assert.Equal(SecurityAlgorithms.HmacSha256, Assert.Single(parameters.ValidAlgorithms));
        Assert.Equal(TimeSpan.FromSeconds(30), parameters.ClockSkew);
        Assert.Null(parameters.LifetimeValidator); Assert.Null(parameters.SignatureValidator);

        var browser = f.Browser(); using var login = await browser.LoginAsync(); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        using var cookieOnly = await browser.SendAsync(HttpMethod.Get, "/test/browser/jwt-identity");
        Assert.Equal(HttpStatusCode.Unauthorized, cookieOnly.StatusCode);
        string token = CreateTestJwt(f, parameters, "valid", userId: f.OtherId);
        using var bearerOnly = await f.Browser().SendAsync(HttpMethod.Get, "/test/browser/jwt-identity", authorization: "Bearer " + token);
        using var bearerAndCookie = await browser.SendAsync(HttpMethod.Get, "/test/browser/jwt-identity", authorization: "Bearer " + token);
        foreach (var response in new[] { bearerOnly, bearerAndCookie })
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            Assert.Equal(f.OtherId, json.RootElement.GetProperty("userId").GetString());
            Assert.Equal(1, json.RootElement.GetProperty("identities").GetInt32());
        }
    }

    [Theory]
    [InlineData("exp", 29, HttpStatusCode.OK)]
    [InlineData("exp", 30, HttpStatusCode.OK)]
    [InlineData("exp", 31, HttpStatusCode.Unauthorized)]
    [InlineData("nbf", 29, HttpStatusCode.OK)]
    [InlineData("nbf", 30, HttpStatusCode.OK)]
    [InlineData("nbf", 31, HttpStatusCode.Unauthorized)]
    public async Task JwtValidationEnforcesThirtySecondClockSkew(string claim, int seconds, HttpStatusCode expected)
    {
        await using var f = await Fixture.CreateAsync();
        var parameters = ProductionJwtParameters(f);
        var clock = new JwtTestClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        // Only supply the clock: leave the production validators, algorithms and 30-second tolerance intact.
        SetJwtTestClock(f, clock);
        DateTime now = clock.GetUtcNow().UtcDateTime;
        string token = SignTestJwt(f.UserId, parameters, now.AddSeconds(claim == "nbf" ? seconds : -300),
            now.AddSeconds(claim == "exp" ? -seconds : 300));
        using var response = await f.Browser().SendAsync(HttpMethod.Get, "/test/browser/jwt-identity", authorization: "Bearer " + token);
        Assert.Equal(expected, response.StatusCode); Assert.Null(response.Headers.Location);
    }

    [Theory]
    [InlineData("exp")]
    [InlineData("nbf")]
    public async Task JwtValidationRejectsOneTickOutsideTheClockSkewBoundary(string claim)
    {
        await using var f = await Fixture.CreateAsync();
        var parameters = ProductionJwtParameters(f);
        var clock = new JwtTestClock(DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds()));
        SetJwtTestClock(f, clock);
        DateTime now = clock.GetUtcNow().UtcDateTime;
        string token = SignTestJwt(f.UserId, parameters, now.AddSeconds(claim == "nbf" ? 30 : -300), now.AddSeconds(claim == "exp" ? -30 : 300));
        var browser = f.Browser();
        using var boundary = await browser.SendAsync(HttpMethod.Get, "/test/browser/jwt-identity", authorization: "Bearer " + token);
        Assert.Equal(HttpStatusCode.OK, boundary.StatusCode);
        clock.Now = clock.Now.AddTicks(claim == "exp" ? 1 : -1);
        using var outside = await browser.SendAsync(HttpMethod.Get, "/test/browser/jwt-identity", authorization: "Bearer " + token);
        Assert.Equal(HttpStatusCode.Unauthorized, outside.StatusCode);
    }

    private static TokenValidationParameters ProductionJwtParameters(Fixture f) =>
        f.Host.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme).TokenValidationParameters;

    private static void SetJwtTestClock(Fixture f, TimeProvider clock)
    {
        var options = f.Host.Services.GetRequiredService<IOptionsMonitor<JwtBearerOptions>>().Get(JwtBearerDefaults.AuthenticationScheme);
        options.TokenValidationParameters = new ClockedJwtValidationParameters(options.TokenValidationParameters, clock);
    }

    private sealed class ClockedJwtValidationParameters : TokenValidationParameters
    {
        private readonly TimeProvider _clock;
        public ClockedJwtValidationParameters(TokenValidationParameters production, TimeProvider clock) : base(production)
        {
            _clock = clock;
            // IdentityModel 8.19.2 keeps its clock internal and resets it in the copy constructor.
            // Preserve the test clock through JwtBearerHandler's Clone, copying all production rules unchanged.
            // Fail if the package removes this seam; never substitute a custom lifetime/signature validator.
            var property = typeof(TokenValidationParameters).GetProperty("TimeProvider", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(property);
            property.SetValue(this, clock);
            Assert.Same(clock, property.GetValue(this));
        }
        public override TokenValidationParameters Clone() => new ClockedJwtValidationParameters(this, _clock);
    }

    private static string CreateTestJwt(Fixture f, TokenValidationParameters parameters, string kind, string? userId = null)
    {
        if (kind == "malformed") return "not-a-jwt";
        if (kind == "malformed-payload") return "eyJhbGciOiJIUzI1NiJ9.bm90LWpzb24.AA";
        DateTime now = DateTime.UtcNow;
        return SignTestJwt(userId ?? f.UserId, parameters,
            now.AddMinutes(kind == "future-nbf" ? 5 : -10), kind == "missing-exp" ? null : now.AddMinutes(kind == "expired" ? -5 : 10),
            kind == "issuer" ? "wrong-issuer" : null, kind == "audience" ? "wrong-audience" : null,
            kind == "signature" ? new SymmetricSecurityKey(RandomNumberGenerator.GetBytes(64)) : null,
            kind == "hs512" ? SecurityAlgorithms.HmacSha512 : SecurityAlgorithms.HmacSha256, kind == "unsigned");
    }

    private static string SignTestJwt(string userId, TokenValidationParameters parameters, DateTime notBefore, DateTime? expires,
        string? issuer = null, string? audience = null, SecurityKey? key = null, string algorithm = SecurityAlgorithms.HmacSha256, bool unsigned = false)
    {
        var token = new JwtSecurityToken(issuer ?? parameters.ValidIssuer, audience ?? parameters.ValidAudience,
            [new Claim(JwtRegisteredClaimNames.Sub, userId)], notBefore, expires,
            unsigned ? null : new SigningCredentials(key ?? parameters.IssuerSigningKey, algorithm));
        // WriteToken preserves a deliberately absent exp; it does not synthesize default timestamps.
        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed class JwtTestClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;
        public override DateTimeOffset GetUtcNow() => Now;
    }
}
