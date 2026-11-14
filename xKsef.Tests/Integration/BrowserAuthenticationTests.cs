using System.Net;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Api.Authentication;
using xKsef.Application.Authentication;
using xKsef.Infrastructure.Authentication;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

[Collection(LocalDbIntegrationCollection.Name)]
public sealed partial class BrowserAuthenticationTests
{
    private const string Password = "Browser-Integration-123!";

    [Fact]
    public async Task LoginIssuesOnlySecureSessionCookieAndMeReturnsTheOwner()
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        Assert.DoesNotContain("token", await login.Content.ReadAsStringAsync(), StringComparison.OrdinalIgnoreCase);
        string cookie = login.Headers.GetValues("Set-Cookie").Last(value => value.StartsWith(BrowserAuthentication.CookieName + "=", StringComparison.Ordinal));
        Assert.Contains("secure", cookie, StringComparison.OrdinalIgnoreCase); Assert.Contains("httponly", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("samesite=lax", cookie, StringComparison.OrdinalIgnoreCase); Assert.Contains("path=/", cookie, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("domain=", cookie, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("expires=", cookie, StringComparison.OrdinalIgnoreCase);
        using var me = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var json = JsonDocument.Parse(await me.Content.ReadAsStringAsync()); Assert.Equal(f.UserId, json.RootElement.GetProperty("userId").GetString());
        Assert.Contains("no-store", me.Headers.CacheControl!.ToString());
        using var scope = f.Scope(); var row = await scope.ServiceProvider.GetRequiredService<AppDbContext>().BrowserSessions.SingleAsync(row => row.UserId == f.UserId);
        Assert.Equal(f.Clock.GetUtcNow().AddHours(8), row.AbsoluteExpiresAt);
    }

    [Fact]
    public async Task LogoutRejectsReplayedOldCookieWithoutRedirects()
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync();
        var replay = browser.Clone(); await browser.CsrfAsync();
        using var logout = await browser.SendAsync(HttpMethod.Post, "/api/browser-auth/logout"); Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        using var denied = await replay.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.Unauthorized, denied.StatusCode); Assert.Null(denied.Headers.Location);
        using var scope = f.Scope(); Assert.NotNull((await scope.ServiceProvider.GetRequiredService<AppDbContext>().BrowserSessions.SingleAsync(row => row.UserId == f.UserId)).RevokedAt);
    }

    [Fact]
    public async Task EverySuccessfulLoginCreatesANewSessionAndRevokesThePreviousOne()
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var first = await browser.LoginAsync(); var old = browser.Clone();
        f.Clock.Advance(TimeSpan.FromMinutes(1)); await browser.CsrfAsync(); using var second = await browser.LoginAsync();
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        using var rejected = await old.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.Unauthorized, rejected.StatusCode);
        using var accepted = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using var scope = f.Scope(); var rows = await scope.ServiceProvider.GetRequiredService<AppDbContext>().BrowserSessions.Where(row => row.UserId == f.UserId).ToArrayAsync();
        Assert.Equal(2, rows.Length); Assert.Single(rows, row => row.RevokedAt is null); Assert.Single(rows, row => row.RevokedAt is not null);
        Assert.Equal(f.Clock.GetUtcNow().AddHours(8), rows.Single(row => row.RevokedAt is null).AbsoluteExpiresAt);
    }

    [Fact]
    public async Task LogoutAllRevokesAllBrowserSessionsOfOnlyThisUser()
    {
        await using var f = await Fixture.CreateAsync(); var one = f.Browser(); var two = f.Browser(); var other = f.Browser();
        using var l1 = await one.LoginAsync(); using var l2 = await two.LoginAsync(); using var l3 = await other.LoginAsync(f.OtherLogin);
        var replay = one.Clone(); await one.CsrfAsync(); using var logout = await one.SendAsync(HttpMethod.Post, "/api/browser-auth/logout-all"); Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        foreach (var browser in new[] { replay, two }) { using var response = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); }
        using var otherMe = await other.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.OK, otherMe.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task IdleAndAbsoluteExpirationAreEnforced(bool absolute)
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync();
        if (absolute)
            for (int i = 0; i < 23; i++) { f.Clock.Advance(TimeSpan.FromMinutes(20)); using var response = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.OK, response.StatusCode); }
        f.Clock.Advance(TimeSpan.FromMinutes(absolute ? 20 : 30));
        using var expired = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Theory]
    [InlineData("register")]
    [InlineData("login")]
    public async Task AnonymousBrowserMutationsRequireCsrf(string action)
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser();
        object body = action == "login" ? new { Login = f.Login, Password } : new { Login = f.Prefix + "-new", Email = f.Prefix + "@example.com", Password };
        using var missing = await browser.SendAsync(HttpMethod.Post, "/api/browser-auth/" + action, body);
        Assert.Equal(HttpStatusCode.BadRequest, missing.StatusCode); Assert.Contains("CSRF", await missing.Content.ReadAsStringAsync());
        await browser.CsrfAsync(); browser.Token = "tampered";
        using var tampered = await browser.SendAsync(HttpMethod.Post, "/api/browser-auth/" + action, body); Assert.Equal(HttpStatusCode.BadRequest, tampered.StatusCode);
        using var scope = f.Scope(); Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().BrowserSessions.AnyAsync(row => row.UserId == f.UserId));
    }

    [Fact]
    public async Task BrowserRegistrationUsesIdentityAndDoesNotGrantRolesTenantsOrAnAutomaticSession()
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); await browser.CsrfAsync();
        var body = new { Login = f.Prefix + "-new", Email = f.Prefix + "@example.com", Password };
        using var registered = await browser.SendAsync(HttpMethod.Post, "/api/browser-auth/register", body); Assert.Equal(HttpStatusCode.Created, registered.StatusCode);
        using var json = JsonDocument.Parse(await registered.Content.ReadAsStringAsync()); string id = json.RootElement.GetProperty("userId").GetString()!;
        using var duplicate = await browser.SendAsync(HttpMethod.Post, "/api/browser-auth/register", body); Assert.Equal(HttpStatusCode.BadRequest, duplicate.StatusCode);
        using var invalid = await browser.SendAsync(HttpMethod.Post, "/api/browser-auth/register", new { Login = f.Prefix + "-invalid", Email = f.Prefix + "-invalid@example.com", Password = "weak" });
        Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode); Assert.Contains("PasswordTooShort", await invalid.Content.ReadAsStringAsync());
        using var me = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.UserRoles.AnyAsync(row => row.UserId == id)); Assert.False(await db.UserTenants.AnyAsync(row => row.UserId == id)); Assert.False(await db.BrowserSessions.AnyAsync(row => row.UserId == id));
    }

    [Theory]
    [InlineData("/api/browser-auth/logout")]
    [InlineData("/api/browser-auth/logout-all")]
    [InlineData("/test/browser/write")]
    [InlineData("/api/tenants")]
    [InlineData("/api/ksef/authentication/certificate")]
    [InlineData("/api/ksef/authentication/authenticate-certificate")]
    [InlineData("/api/ksef/authentication/authenticate-token")]
    [InlineData("/api/ksef/credentials/token")]
    [InlineData("/api/invoices/import/ksef")]
    public async Task AuthenticatedCookieMutationsCannotBypassCsrf(string path)
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync(); browser.Token = null; var tenant = await f.CreateTenantAsync();
        using var result = await browser.SendAsync(HttpMethod.Post, path, new { Name = "unused", Nip = "1234567890", TaxEntityName = "unused" }, tenantId: tenant);
        Assert.Equal(HttpStatusCode.BadRequest, result.StatusCode); Assert.Contains("CSRF", await result.Content.ReadAsStringAsync()); Assert.Equal(0, f.Gate.Writes);
    }

    [Theory]
    [InlineData("Bearer malformed")]
    [InlineData("Basic abc")]
    [InlineData("Bearer")]
    public async Task PresenceOfInvalidAuthorizationNeverFallsBackToValidCookie(string header)
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync();
        using var response = await browser.SendAsync(HttpMethod.Get, "/test/browser/identity", authorization: header);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode); Assert.Null(response.Headers.Location);
        using var me = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me", authorization: header); Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task ValidBearerIsNotMergedWithCookieAndCanMutateWithoutCsrf()
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync();
        string jwt; using (var scope = f.Scope()) jwt = scope.ServiceProvider.GetRequiredService<IJwtTokenService>().GenerateToken(f.OtherId, f.OtherLogin);
        browser.Token = null; using var response = await browser.SendAsync(HttpMethod.Post, "/test/browser/write", new { }, "Bearer " + jwt);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode); using var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(f.OtherId, json.RootElement.GetProperty("userId").GetString()); Assert.Equal(1, json.RootElement.GetProperty("identities").GetInt32());
        using var cookieMe = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.OK, cookieMe.StatusCode);
        var wpf = f.Browser(); using var legacy = await wpf.SendAsync(HttpMethod.Post, "/api/auth/login", new { Login = f.Login, Password });
        Assert.Equal(HttpStatusCode.OK, legacy.StatusCode); using var legacyJson = JsonDocument.Parse(await legacy.Content.ReadAsStringAsync()); Assert.Equal("Bearer", legacyJson.RootElement.GetProperty("tokenType").GetString());
        Assert.False(legacy.Headers.Contains("Set-Cookie"));
    }

    [Fact]
    public async Task CsrfTokenMustMatchTheAuthenticatedUserAndNewTokenAllowsBusinessWrites()
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync();
        using var anonymousToken = await browser.SendAsync(HttpMethod.Post, "/test/browser/write", new { }); Assert.Equal(HttpStatusCode.BadRequest, anonymousToken.StatusCode);
        await browser.CsrfAsync(); using var accepted = await browser.SendAsync(HttpMethod.Post, "/test/browser/write", new { }); Assert.Equal(HttpStatusCode.OK, accepted.StatusCode); Assert.Equal(1, f.Gate.Writes);
        var other = f.Browser(); using var otherLogin = await other.LoginAsync(f.OtherLogin); other.Token = browser.Token;
        using var stolen = await other.SendAsync(HttpMethod.Post, "/test/browser/write", new { }); Assert.Equal(HttpStatusCode.BadRequest, stolen.StatusCode); Assert.Equal(1, f.Gate.Writes);
    }

    [Fact]
    public async Task TenantMembershipIsCheckedOnEveryCookieRequest()
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync(); var tenant = await f.CreateTenantAsync();
        using var accepted = await browser.SendAsync(HttpMethod.Get, "/test/browser/tenant", tenantId: tenant); Assert.Equal(HttpStatusCode.OK, accepted.StatusCode);
        using (var scope = f.Scope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserTenants.Where(row => row.TenantId == tenant && row.UserId == f.UserId).ExecuteDeleteAsync();
        using var revoked = await browser.SendAsync(HttpMethod.Get, "/test/browser/tenant", tenantId: tenant); Assert.Equal(HttpStatusCode.Forbidden, revoked.StatusCode); Assert.Null(revoked.Headers.Location);
        using var me = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PasswordChangesAndResetsInvalidateAllOldSessions(bool reset)
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync();
        using (var scope = f.Scope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await manager.FindByIdAsync(f.UserId))!;
            IdentityResult result = reset ? await manager.ResetPasswordAsync(user, await manager.GeneratePasswordResetTokenAsync(user), Password + "-changed")
                : await manager.ChangePasswordAsync(user, Password, Password + "-changed"); Assert.True(result.Succeeded);
        }
        using var expired = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.Unauthorized, expired.StatusCode);
    }

    [Fact]
    public async Task DeletedAccountCannotUseItsSession()
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync();
        using (var scope = f.Scope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(user => user.Id == f.UserId).ExecuteDeleteAsync();
        using var me = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    [Fact]
    public async Task LogoutWinsAgainstAResponseThatRenewsTheSessionLater()
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        f.Clock.Advance(TimeSpan.FromMinutes(20)); var late = browser.Clone(); var old = browser.Clone();
        Task<HttpResponseMessage> pending = late.SendAsync(HttpMethod.Get, "/test/browser/slow");
        try
        {
            await f.Gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            using var logout = await browser.SendAsync(HttpMethod.Post, "/api/browser-auth/logout"); Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
        }
        finally { f.Gate.Release.TrySetResult(); }
        using var finished = await pending; Assert.Equal(HttpStatusCode.OK, finished.StatusCode);
        foreach (var replay in new[] { late, old }) { using var me = await replay.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode); }
    }

    [Fact]
    public async Task CookieAndSqlSessionSurviveAHostRestartWithPersistedProtectedKeys()
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync();
        Assert.NotEmpty(Directory.EnumerateFiles(f.KeyPath, "key-*.xml"));
        await f.RestartAsync(); using var me = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        using var scope = f.Scope(); Assert.Equal(1, await scope.ServiceProvider.GetRequiredService<AppDbContext>().BrowserSessions.CountAsync(row => row.UserId == f.UserId));
    }

    [Fact]
    public async Task StoreFailureReturns503AndNeverAdmitsTheRequestOrFallsBack()
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync(); f.Failure.Enabled = true;
        using var failed = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.ServiceUnavailable, failed.StatusCode);
        string body = await failed.Content.ReadAsStringAsync(); Assert.DoesNotContain("SELECT", body, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("simulated", body);
        using var denied = await browser.SendAsync(HttpMethod.Post, "/test/browser/write", new { }); Assert.Equal(HttpStatusCode.ServiceUnavailable, denied.StatusCode); Assert.Equal(0, f.Gate.Writes);
        f.Failure.Enabled = false; using var recovered = await browser.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.OK, recovered.StatusCode);
    }

    [Theory]
    [InlineData("login")]
    [InlineData("register")]
    public async Task LegacyAndBrowserRoutesShareTheSameRateLimitBucket(string action)
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); await browser.CsrfAsync();
        object body = action == "login" ? new { Login = f.Prefix + "-missing", Password } : new { Login = f.Prefix + "-invalid", Email = f.Prefix + "@example.com", Password = "weak" };
        for (int i = 0; i < 5; i++)
        {
            using var response = await browser.SendAsync(HttpMethod.Post, "/api/" + (i % 2 == 0 ? "auth/" : "browser-auth/") + action, body);
            Assert.Equal(action == "login" ? HttpStatusCode.Unauthorized : HttpStatusCode.BadRequest, response.StatusCode);
        }
        using var limited = await browser.SendAsync(HttpMethod.Post, "/api/browser-auth/" + action, body); Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        using var legacyLimited = await browser.SendAsync(HttpMethod.Post, "/api/auth/" + action, body); Assert.Equal(HttpStatusCode.TooManyRequests, legacyLimited.StatusCode);
    }

    [Fact]
    public async Task StampChangedAfterPasswordVerificationCannotCreateAValidSession()
    {
        await using var f = await Fixture.CreateAsync(); string oldStamp;
        using (var scope = f.Scope())
        {
            var manager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>(); var user = (await manager.FindByIdAsync(f.UserId))!; oldStamp = user.SecurityStamp!;
            Assert.True((await manager.UpdateSecurityStampAsync(user)).Succeeded);
        }
        using var s = f.Scope(); var ticket = new Microsoft.AspNetCore.Authentication.AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity(new[] {
            new Claim(ClaimTypes.NameIdentifier, f.UserId), new Claim("AspNet.Identity.SecurityStamp", oldStamp) }, BrowserSessionOptions.AuthenticationScheme)),
            new Microsoft.AspNetCore.Authentication.AuthenticationProperties(), BrowserSessionOptions.AuthenticationScheme);
        await Assert.ThrowsAsync<BrowserSessionIdentityChangedException>(() => s.ServiceProvider.GetRequiredService<SqlBrowserTicketStore>().StoreAsync(ticket));
    }
    [Fact]
    public async Task EmptyAuthorizationHeaderReceivedByTheServerAlsoSelectsBearer()
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync();
        // HttpClient drops empty header values. Send the raw server request to exercise actual header presence.
        var context = await Microsoft.AspNetCore.TestHost.HostBuilderTestServerExtensions.GetTestServer(f.Host).SendAsync(http =>
        {
            http.Request.Scheme = "https"; http.Request.Method = "GET"; http.Request.Path = "/test/browser/identity";
            http.Request.Headers.Cookie = browser.CookieHeader; http.Request.Headers["Authorization"] = "";
            Assert.True(http.Request.Headers.ContainsKey("Authorization"));
        });
        Assert.Equal(401, context.Response.StatusCode);
    }
    [Fact]
    public async Task ScheduledCleanupDeletesExpiredSessionsInBoundedBatchesAndKeepsActiveSessions()
    {
        await using var f = await Fixture.CreateAsync(); var first = f.Browser(); var second = f.Browser();
        using var l1 = await first.LoginAsync(); using var l2 = await second.LoginAsync();
        // The scheduled operation is global: do not enable it in this test if any unrelated sessions exist.
        using (var scope = f.Scope()) Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().BrowserSessions.AnyAsync(row => row.UserId != f.UserId));
        f.Clock.Advance(TimeSpan.FromMinutes(31)); var active = f.Browser(); using var l3 = await active.LoginAsync(); Assert.Equal(HttpStatusCode.OK, l3.StatusCode);
        await f.EnableCleanupAsync();
        int count = 3;
        for (int i = 0; i < 100 && count != 1; i++)
        {
            await Task.Delay(20); using var scope = f.Scope(); count = await scope.ServiceProvider.GetRequiredService<AppDbContext>().BrowserSessions.CountAsync(row => row.UserId == f.UserId);
        }
        Assert.Equal(1, count); using var me = await active.SendAsync(HttpMethod.Get, "/api/browser-auth/me"); Assert.Equal(HttpStatusCode.OK, me.StatusCode);
    }
    [Fact]
    public async Task InvalidBearerCannotUseCookieOrBypassCsrfOnAnonymousLegacyRoutes()
    {
        await using var f = await Fixture.CreateAsync(); var browser = f.Browser(); using var login = await browser.LoginAsync(); browser.Token = null;
        using var shared = await browser.SendAsync(HttpMethod.Get, "/test/browser/identity"); Assert.Contains("no-store", shared.Headers.CacheControl!.ToString());
        using var badLogin = await browser.SendAsync(HttpMethod.Post, "/api/auth/login", new { Login = f.Login, Password }, "Bearer invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, badLogin.StatusCode);
        using var badRegister = await browser.SendAsync(HttpMethod.Post, "/api/auth/register", new { Login = f.Prefix + "-rejected", Email = f.Prefix + "@example.com", Password }, "Bearer invalid");
        Assert.Equal(HttpStatusCode.Unauthorized, badRegister.StatusCode);
        using var scope = f.Scope(); Assert.Equal(2, await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.CountAsync(user => user.UserName != null && user.UserName.StartsWith(f.Prefix)));
    }
}
