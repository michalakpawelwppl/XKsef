using System.Net;
using System.Text;
using System.Xml.Linq;
using Microsoft.Extensions.Options;
using xKsef.Application.Gus;
using xKsef.Infrastructure.Gus;

namespace xKsef.Tests.Infrastructure;

public sealed partial class GusBirClientTests
{
    [Theory]
    [InlineData("search-code-7")]
    [InlineData("diagnostic-code-7")]
    [InlineData("diagnostic-empty")]
    public async Task RenewalConfirmedSidErrorRelogsOnceAndRepeatsLookupWithSameBudget(string failure)
    {
        var clock = new IntegrationClock();
        var limiter = new RecordingLimiter(clock);
        var renewedSid = new string('B', 20);
        var logins = 0;
        var diagnostic = failure != "search-code-7";
        using var handler = new StubHandler((request, _) =>
        {
            var result = request.Operation == "Zaloguj" ? ++logins == 1 ? Sid : renewedSid
                : request.Sid == Sid ? request.Operation == "GetValue" ? failure == "diagnostic-empty" ? "" : "7" : diagnostic ? "" : SidErrorXml
                : request.Operation == "GetValue" ? "4" : diagnostic ? "" : EntityXml;
            return Task.FromResult(Reply(request.Operation, result));
        });
        var client = LimitedClient(handler, clock, limiter);
        var result = await client.SearchByNipAsync("8567346215");
        if (diagnostic) Assert.Empty(result); else Assert.Single(result);
        var expected = diagnostic
            ? new[] { "Zaloguj", "DaneSzukajPodmioty", "GetValue", "Zaloguj", "DaneSzukajPodmioty", "GetValue" }
            : new[] { "Zaloguj", "DaneSzukajPodmioty", "Zaloguj", "DaneSzukajPodmioty" };
        Assert.Equal(expected, handler.Requests.Select(r => r.Operation));
        var searches = handler.Requests.Where(r => r.Operation == "DaneSzukajPodmioty").ToArray();
        Assert.Equal(Sid, searches[0].Sid);
        Assert.Equal(renewedSid, searches[1].Sid);
        Assert.Equal(searches[0].Body.Descendants(XName.Get("pParametryWyszukiwania", Service)).Single().ToString(),
            searches[1].Body.Descendants(XName.Get("pParametryWyszukiwania", Service)).Single().ToString());
        Assert.Equal(expected.Length, limiter.Permits.Count);
        Assert.All(limiter.Permits, permit => Assert.False(permit.TryUse()));
        Assert.All(limiter.Budgets, budget => Assert.Same(limiter.Budgets[0], budget));
        await client.SearchByNipAsync("8567346215");
        Assert.Equal(2, logins); // The old rejection cannot clear the newly established SID.
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RenewalSecondSidRejectionEndsLookupWithoutThirdLogin(bool diagnostic)
    {
        var clock = new IntegrationClock();
        var limiter = new RecordingLimiter(clock);
        var logins = 0;
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation,
            request.Operation == "Zaloguj" ? new string(++logins == 1 ? 'A' : 'B', 20)
            : request.Operation == "GetValue" ? "7" : diagnostic ? "" : SidErrorXml)));
        var error = await Assert.ThrowsAsync<GusBirException>(() => LimitedClient(handler, clock, limiter).SearchByNipAsync("8567346215"));
        Assert.Equal(GusBirError.SessionExpired, error.Error);
        Assert.Equal(7, error.ServiceCode);
        Assert.Equal(2, logins);
        Assert.Equal(diagnostic ? 6 : 4, handler.Requests.Count);
        Assert.Equal(handler.Requests.Count, limiter.Permits.Count);
    }

    [Theory]
    [InlineData("soap-fault", GusBirError.ServiceFault)]
    [InlineData("unauthorized", GusBirError.Communication)]
    [InlineData("forbidden", GusBirError.Communication)]
    [InlineData("data-error", GusBirError.ServiceRejected)]
    [InlineData("whitespace-diagnostic", GusBirError.InvalidResponse)]
    [InlineData("text-diagnostic", GusBirError.InvalidResponse)]
    [InlineData("network", GusBirError.Communication)]
    public async Task RenewalDoesNotInferSidFailureFromOtherErrorsOrMessageText(string failure, GusBirError expected)
    {
        var clock = new IntegrationClock();
        var limiter = new RecordingLimiter(clock);
        using var handler = new StubHandler((request, _) =>
        {
            if (request.Operation == "Zaloguj") return Task.FromResult(Reply("Zaloguj", Sid));
            if (failure == "soap-fault") return Task.FromResult(new HttpResponseMessage(HttpStatusCode.InternalServerError)
            {
                Content = new StringContent($"<s:Envelope xmlns:s=\"{Soap}\"><s:Body><s:Fault><s:Reason><s:Text>Invalid SID expired session code 7</s:Text></s:Reason></s:Fault></s:Body></s:Envelope>", Encoding.UTF8, "application/soap+xml")
            });
            if (failure is "unauthorized" or "forbidden") return Task.FromResult(new HttpResponseMessage(failure == "unauthorized" ? HttpStatusCode.Unauthorized : HttpStatusCode.Forbidden));
            if (failure == "network") throw new HttpRequestException("session expired");
            return Task.FromResult(Reply(request.Operation, request.Operation == "GetValue" ? failure == "whitespace-diagnostic" ? " \r\n " : "SID expired"
                : failure == "data-error" ? "<root><dane><ErrorCode>2</ErrorCode><ErrorMessagePl>SID expired</ErrorMessagePl></dane></root>" : ""));
        });
        var error = await Assert.ThrowsAsync<GusBirException>(() => LimitedClient(handler, clock, limiter).SearchByNipAsync("8567346215"));
        Assert.Equal(expected, error.Error);
        Assert.Single(handler.Requests, r => r.Operation == "Zaloguj");
    }

    [Fact]
    public async Task RenewalHttpTimeoutDoesNotRetrySearch()
    {
        using var handler = new StubHandler(async (request, token) =>
        {
            if (request.Operation == "Zaloguj") return Reply("Zaloguj", Sid);
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable");
        });
        var client = new GusBirClient(new StubHttpClientFactory(handler),
            Options.Create(new GusBirOptions { TimeoutSeconds = 1 }), TimeProvider.System, CreateScopeFactory());
        var error = await Assert.ThrowsAsync<GusBirException>(() => client.SearchByNipAsync("8567346215"));
        Assert.Equal(GusBirError.Timeout, error.Error);
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty" }, handler.Requests.Select(r => r.Operation));
    }

    [Fact]
    public async Task RenewalHttpCancellationDoesNotRetrySearch()
    {
        var clock = new IntegrationClock();
        var limiter = new RecordingLimiter(clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, token) =>
        {
            if (request.Operation == "Zaloguj") return Reply("Zaloguj", Sid);
            entered.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            throw new InvalidOperationException("Unreachable");
        });
        using var caller = new CancellationTokenSource();
        var pending = LimitedClient(handler, clock, limiter).SearchByNipAsync("8567346215", caller.Token);
        await entered.Task;
        caller.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty" }, handler.Requests.Select(r => r.Operation));
        Assert.Equal(2, limiter.Permits.Count);
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("limit")]
    [InlineData("cancel")]
    public async Task RenewalFailureWhileAcquiringReloginPermitStopsWithoutAnotherAttempt(string failure)
    {
        var clock = new IntegrationClock();
        using var caller = new CancellationTokenSource();
        Exception cause = failure == "sql" ? new GusRateLimiterUnavailableException() : new GusRateLimitExceededException(TimeSpan.FromSeconds(3));
        var limiter = new RecordingLimiter(clock, (attempt, _, token) =>
        {
            if (attempt != 3) return Task.FromResult(FreshPermit(clock));
            if (failure == "cancel") { caller.Cancel(); token.ThrowIfCancellationRequested(); }
            return Task.FromException<GusCallPermit>(cause);
        });
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : SidErrorXml)));
        var pending = LimitedClient(handler, clock, limiter).SearchByNipAsync("8567346215", caller.Token);
        if (failure == "cancel") await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        else Assert.Same(cause, await Record.ExceptionAsync(() => pending));
        Assert.Equal(3, limiter.Budgets.Count);
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty" }, handler.Requests.Select(r => r.Operation));
    }

    [Fact]
    public async Task RenewalDoesNotResetTenSecondBudget()
    {
        var clock = new IntegrationClock();
        var limiter = new RecordingLimiter(clock, (_, budget, token) =>
        {
            using var waiting = budget.BeginWaiting(token);
            clock.Advance(TimeSpan.FromSeconds(3));
            waiting.ThrowIfCancellationRequested();
            return Task.FromResult(FreshPermit(clock));
        });
        var logins = 0;
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation,
            request.Operation == "Zaloguj" ? new string(++logins == 1 ? 'A' : 'B', 20) : SidErrorXml)));
        await Assert.ThrowsAsync<GusRateLimitExceededException>(() => LimitedClient(handler, clock, limiter).SearchByNipAsync("8567346215"));
        Assert.Equal(4, limiter.Budgets.Count);
        Assert.All(limiter.Budgets, budget => Assert.Same(limiter.Budgets[0], budget));
        Assert.Equal(TimeSpan.Zero, limiter.Budgets[0].Remaining);
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty", "Zaloguj" }, handler.Requests.Select(r => r.Operation));
    }

    [Fact]
    public async Task RenewalSqlFailureAfterNewLoginDoesNotClearTheNewSid()
    {
        var clock = new IntegrationClock();
        var cause = new GusRateLimiterUnavailableException();
        var limiter = new RecordingLimiter(clock, (attempt, _, _) => attempt == 4
            ? Task.FromException<GusCallPermit>(cause) : Task.FromResult(FreshPermit(clock)));
        var logins = 0;
        var renewedSid = new string('B', 20);
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation,
            request.Operation == "Zaloguj" ? ++logins == 1 ? Sid : renewedSid : request.Sid == Sid ? SidErrorXml : EntityXml)));
        var client = LimitedClient(handler, clock, limiter);
        Assert.Same(cause, await Record.ExceptionAsync(() => client.SearchByNipAsync("8567346215")));
        Assert.Equal(3, handler.Requests.Count);
        Assert.Single(await client.SearchByNipAsync("8567346215"));
        Assert.Equal(2, logins);
        Assert.Equal(renewedSid, handler.Requests.Last().Sid);
    }

    [Fact]
    public async Task RenewalConcurrentLookupsUseNewSidWithoutLoginStormOrStaleInvalidation()
    {
        var clock = new IntegrationClock();
        var limiter = new RecordingLimiter(clock);
        var logins = 0;
        var renewedSid = new string('B', 20);
        var rejected = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, _) =>
        {
            if (request.Operation == "Zaloguj") return Reply("Zaloguj", ++logins == 1 ? Sid : renewedSid);
            if (request.Sid == Sid)
            {
                rejected.TrySetResult();
                await release.Task;
                return Reply(request.Operation, SidErrorXml);
            }
            Assert.Equal(renewedSid, request.Sid);
            await Task.Yield();
            return Reply(request.Operation, EntityXml);
        });
        var client = LimitedClient(handler, clock, limiter);
        var first = client.SearchByNipAsync("8567346215");
        await rejected.Task;
        var others = Enumerable.Range(0, 12).Select(_ => client.SearchByNipAsync("8567346215")).ToArray();
        release.TrySetResult();
        await Task.WhenAll(others.Append(first));
        Assert.Equal(2, logins);
        Assert.Single(handler.Requests, r => r.Sid == Sid);
        Assert.Equal(13, handler.Requests.Count(r => r.Sid == renewedSid));
        await client.SearchByNipAsync("8567346215");
        Assert.Equal(2, logins);
    }

    [Fact]
    public async Task RenewalLogoutIsNeverAutomaticallyRetried()
    {
        var clock = new IntegrationClock();
        var limiter = new RecordingLimiter(clock);
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : "false")));
        var client = LimitedClient(handler, clock, limiter);
        await client.LoginAsync();
        await Assert.ThrowsAsync<GusBirException>(() => client.LogoutAsync());
        Assert.Equal(new[] { "Zaloguj", "Wyloguj" }, handler.Requests.Select(r => r.Operation));
    }

    private const string SidErrorXml = "<root><dane><ErrorCode>7</ErrorCode><ErrorMessagePl>Untrusted text</ErrorMessagePl></dane></root>";
}
