using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using xKsef.Application.Gus;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Gus;

namespace xKsef.Tests.Infrastructure;

public sealed partial class GusBirClientTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LimiterEverySoapHasItsOwnConsumedPermitAndCorrectEnvironment(bool production)
    {
        var clock = new IntegrationClock();
        var limiter = new RecordingLimiter(clock);
        var sentPermits = new HashSet<GusCallPermit>();
        using var handler = new StubHandler((request, _) =>
        {
            var permit = limiter.Permits.Last();
            Assert.False(permit.IsUsable);
            Assert.False(permit.TryUse());
            Assert.True(sentPermits.Add(permit));
            return Task.FromResult(Reply(request.Operation, request.Operation switch { "Zaloguj" => Sid, "GetValue" => "4", "Wyloguj" => "true", _ => "" }));
        });
        var options = new GusBirOptions { Environment = production ? GusBirEnvironment.Production : GusBirEnvironment.Test, ApiKey = production ? "unitTestOnlyKey12345" : null };
        var client = new GusBirClient(new StubHttpClientFactory(handler), Options.Create(options), clock, CreateScopeFactory(limiter));
        await client.SearchByNipAsync("8567346215");
        await client.SearchByRegonAsync("012345675");
        await client.LogoutAsync();
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty", "GetValue", "DaneSzukajPodmioty", "GetValue", "Wyloguj" }, handler.Requests.Select(r => r.Operation));
        Assert.Equal(6, limiter.Budgets.Count);
        Assert.Equal(6, sentPermits.Count);
        Assert.All(limiter.Environments, environment => Assert.Equal(production ? GusRateLimitEnvironment.Production : GusRateLimitEnvironment.Test, environment));
        Assert.Same(limiter.Budgets[0], limiter.Budgets[1]);
        Assert.Same(limiter.Budgets[1], limiter.Budgets[2]);
        Assert.Same(limiter.Budgets[3], limiter.Budgets[4]);
        Assert.NotSame(limiter.Budgets[0], limiter.Budgets[3]);
        Assert.NotSame(limiter.Budgets[3], limiter.Budgets[5]);
    }

    [Theory]
    [InlineData("limit")]
    [InlineData("sql")]
    [InlineData("technical")]
    public async Task LimiterFailurePreventsHttpAndPropagatesWithoutRemappingOrRetry(string kind)
    {
        var clock = new IntegrationClock();
        Exception cause = kind switch
        {
            "limit" => new GusRateLimitExceededException(TimeSpan.FromSeconds(30)),
            "sql" => new GusRateLimiterUnavailableException(new IOException("SQL unavailable")),
            _ => new IOException("Technical store failure")
        };
        var limiter = new RecordingLimiter(clock, (_, _, _) => Task.FromException<GusCallPermit>(cause));
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException("HTTP must not run"));
        var client = LimitedClient(handler, clock, limiter);
        Assert.Same(cause, await Record.ExceptionAsync(() => client.SearchByNipAsync("8567346215")));
        Assert.Single(limiter.Budgets);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task LimiterSqlFailureBeforeSearchDoesNotDiscardValidSid()
    {
        var clock = new IntegrationClock();
        var cause = new GusRateLimiterUnavailableException();
        var limiter = new RecordingLimiter(clock, (attempt, _, _) => attempt == 2 ? Task.FromException<GusCallPermit>(cause) : Task.FromResult(FreshPermit(clock)));
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : EntityXml)));
        var client = LimitedClient(handler, clock, limiter);
        Assert.Same(cause, await Record.ExceptionAsync(() => client.SearchByNipAsync("8567346215")));
        Assert.Equal("Zaloguj", Assert.Single(handler.Requests).Operation);
        await client.SearchByNipAsync("8567346215");
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty" }, handler.Requests.Select(r => r.Operation));
        Assert.Equal(3, limiter.Budgets.Count);
    }

    [Fact]
    public async Task LimiterFailureBeforeGetValuePreventsDiagnosticHttp()
    {
        var clock = new IntegrationClock();
        var cause = new GusRateLimitExceededException(TimeSpan.FromSeconds(1));
        var limiter = new RecordingLimiter(clock, (attempt, _, _) => attempt == 3 ? Task.FromException<GusCallPermit>(cause) : Task.FromResult(FreshPermit(clock)));
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : "")));
        Assert.Same(cause, await Record.ExceptionAsync(() => LimitedClient(handler, clock, limiter).SearchByNipAsync("8567346215")));
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty" }, handler.Requests.Select(r => r.Operation));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LimiterReplacesExpiredOrPreviouslyUsedPermitWithoutRepeatingSoap(bool used)
    {
        var clock = new IntegrationClock();
        var shared = FreshPermit(clock);
        var limiter = new RecordingLimiter(clock, (attempt, _, _) => Task.FromResult(
            used && attempt <= 2 ? shared : !used && attempt == 1 ? new GusCallPermit(clock.GetUtcNow().AddSeconds(-1), TimeSpan.FromMilliseconds(250), clock) : FreshPermit(clock)));
        using var handler = new StubHandler((request, _) => Task.FromResult(Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : EntityXml)));
        await LimitedClient(handler, clock, limiter).SearchByNipAsync("8567346215");
        Assert.Equal(3, limiter.Budgets.Count);
        Assert.Equal(new[] { "Zaloguj", "DaneSzukajPodmioty" }, handler.Requests.Select(r => r.Operation));
        Assert.All(limiter.Budgets, budget => Assert.Same(limiter.Budgets[0], budget));
    }

    [Fact]
    public async Task LimiterRepeatedExpiredPermitsCannotResetWaitingBudget()
    {
        var clock = new IntegrationClock();
        var limiter = new RecordingLimiter(clock, (_, budget, token) =>
        {
            using var waiting = budget.BeginWaiting(token);
            clock.Advance(TimeSpan.FromSeconds(1));
            waiting.ThrowIfCancellationRequested();
            return Task.FromResult(new GusCallPermit(clock.GetUtcNow().AddSeconds(-1), TimeSpan.FromMilliseconds(250), clock));
        });
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException("HTTP must not run"));
        await Assert.ThrowsAsync<GusRateLimitExceededException>(() => LimitedClient(handler, clock, limiter).SearchByNipAsync("8567346215"));
        Assert.Equal(10, limiter.Budgets.Count);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public async Task LimiterBudgetSpansLoginSearchAndDiagnosticsButExcludesHttp(int waitSeconds, bool exhausted)
    {
        var clock = new IntegrationClock();
        var limiter = new RecordingLimiter(clock, (_, budget, token) =>
        {
            using var waiting = budget.BeginWaiting(token);
            clock.Advance(TimeSpan.FromSeconds(waitSeconds));
            waiting.ThrowIfCancellationRequested();
            return Task.FromResult(FreshPermit(clock));
        });
        using var handler = new StubHandler((request, _) =>
        {
            clock.Advance(TimeSpan.FromMinutes(2)); // Simulated HTTP duration, outside any waiting scope.
            return Task.FromResult(Reply(request.Operation, request.Operation switch { "Zaloguj" => Sid, "GetValue" => "4", _ => "" }));
        });
        var pending = LimitedClient(handler, clock, limiter).SearchByNipAsync("8567346215");
        if (exhausted) await Assert.ThrowsAsync<GusRateLimitExceededException>(() => pending);
        else Assert.Empty(await pending);
        Assert.Equal(3, limiter.Budgets.Count);
        Assert.All(limiter.Budgets, budget => Assert.Same(limiter.Budgets[0], budget));
        Assert.Equal(TimeSpan.FromSeconds(exhausted ? 0 : 1), limiter.Budgets[0].Remaining);
        Assert.Equal(exhausted ? 2 : 3, handler.Requests.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LimiterSessionGateWaitUsesBudgetAndSupportsCallerCancellation(bool cancelCaller)
    {
        var clock = new IntegrationClock();
        var limiter = new RecordingLimiter(clock);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var handler = new StubHandler(async (request, _) =>
        {
            if (request.Operation != "Zaloguj") { entered.TrySetResult(); await release.Task; }
            return Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : EntityXml);
        });
        var client = LimitedClient(handler, clock, limiter);
        var first = client.SearchByNipAsync("8567346215");
        await entered.Task;
        using var caller = new CancellationTokenSource();
        try
        {
            var second = client.SearchByNipAsync("8567346215", caller.Token);
            Assert.False(second.IsCompleted);
            if (cancelCaller) caller.Cancel(); else clock.Advance(TimeSpan.FromSeconds(10));
            if (cancelCaller) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => second);
            else await Assert.ThrowsAsync<GusRateLimitExceededException>(() => second);
            Assert.Equal(2, limiter.Budgets.Count); // Only the first lookup has reached the limiter.
            Assert.Equal(2, handler.Requests.Count);
        }
        finally { release.TrySetResult(); await first; }
        await client.SearchByNipAsync("8567346215"); // Neither failure leaves the gate locked.
        Assert.Single(handler.Requests, r => r.Operation == "Zaloguj");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LimiterPendingAdmissionIsInterruptedByCallerOrBudget(bool cancelCaller)
    {
        var clock = new IntegrationClock();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var limiter = new RecordingLimiter(clock, async (_, budget, token) =>
        {
            using var waiting = budget.BeginWaiting(token);
            entered.TrySetResult();
            try { await Task.Delay(Timeout.InfiniteTimeSpan, waiting.CancellationToken); }
            catch (OperationCanceledException) { waiting.ThrowIfCancellationRequested(); throw; }
            throw new InvalidOperationException("Unreachable");
        });
        using var handler = new StubHandler((_, _) => throw new InvalidOperationException("HTTP must not run"));
        using var caller = new CancellationTokenSource();
        var pending = LimitedClient(handler, clock, limiter).SearchByNipAsync("8567346215", caller.Token);
        await entered.Task;
        if (cancelCaller) caller.Cancel(); else clock.Advance(TimeSpan.FromSeconds(10));
        if (cancelCaller) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        else await Assert.ThrowsAsync<GusRateLimitExceededException>(() => pending);
        Assert.Empty(handler.Requests);
        Assert.Single(limiter.Budgets);
    }

    [Fact]
    public async Task LimiterRealDiUsesOneScopedLimiterPerOperationAndPreservesSingletonSession()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=offline.invalid;Database=NeverOpened;Integrated Security=true"
        }).Build();
        var created = 0;
        var disposed = 0;
        var permits = new List<GusCallPermit>();
        using var handler = new StubHandler(async (request, _) =>
        {
            await Task.Yield();
            Assert.False(permits.Last().IsUsable);
            return Reply(request.Operation, request.Operation == "Zaloguj" ? Sid : EntityXml);
        });
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDatabaseInfrastructure(configuration);
        services.AddGusBirInfrastructure(configuration);
        services.AddScoped<IGusQuotaStore>(_ =>
        {
            created++;
            return new IntegrationQuotaStore(permits, () => disposed++);
        });
        services.AddHttpClient(GusBirServiceCollectionExtensions.HttpClientName).ConfigurePrimaryHttpMessageHandler(() => handler);
        await using var provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<IGusRateLimiter>());
        var client = provider.GetRequiredService<IGusBirClient>();
        await Task.WhenAll(Enumerable.Range(0, 8).Select(async _ =>
        {
            using var scope = provider.CreateScope();
            Assert.Same(client, scope.ServiceProvider.GetRequiredService<IGusBirClient>());
            await client.SearchByNipAsync("8567346215");
        }));
        Assert.Equal(8, created);
        Assert.Equal(created, disposed);
        Assert.Equal(9, permits.Count); // Login + eight searches, with only eight DI scopes.
        Assert.Single(handler.Requests, r => r.Operation == "Zaloguj");
        Assert.All(handler.Requests.Skip(1), r => Assert.Equal(Sid, r.Sid));
    }

    private GusBirClient LimitedClient(StubHandler handler, IntegrationClock clock, IGusRateLimiter limiter)
        => new(new StubHttpClientFactory(handler), Options.Create(new GusBirOptions()), clock, CreateScopeFactory(limiter));
    private static GusCallPermit FreshPermit(TimeProvider clock) => new(clock.GetUtcNow(), TimeSpan.FromMilliseconds(250), clock);

    private sealed class RecordingLimiter(IntegrationClock clock, Func<int, GusWaitBudget, CancellationToken, Task<GusCallPermit>>? acquire = null) : IGusRateLimiter
    {
        public List<GusWaitBudget> Budgets { get; } = [];
        public List<GusRateLimitEnvironment> Environments { get; } = [];
        public List<GusCallPermit> Permits { get; } = [];
        public async Task<GusCallPermit> AcquireAsync(GusRateLimitEnvironment environment, GusWaitBudget budget, CancellationToken cancellationToken = default)
        {
            Budgets.Add(budget);
            Environments.Add(environment);
            budget.ThrowIfExhausted(cancellationToken);
            var permit = acquire is null ? FreshPermit(clock) : await acquire(Budgets.Count, budget, cancellationToken);
            Permits.Add(permit);
            return permit;
        }
    }

    private sealed class IntegrationQuotaStore(List<GusCallPermit> permits, Action dispose) : IGusQuotaStore, IDisposable
    {
        public Task<GusQuotaAttempt> TryAcquireAsync(GusRateLimitEnvironment environment, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var permit = FreshPermit(TimeProvider.System);
            permits.Add(permit);
            return Task.FromResult(GusQuotaAttempt.Granted(permit));
        }
        public void Dispose() => dispose();
    }

    private sealed class IntegrationClock : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<ClockTimer> _timers = [];
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (_gate) return _ticks; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + TimeSpan.FromTicks(GetTimestamp());
        public void Advance(TimeSpan elapsed)
        {
            ClockTimer[] due;
            lock (_gate)
            {
                _ticks += elapsed.Ticks;
                due = _timers.Where(t => t.DueAt is { } at && at <= _ticks).ToArray();
                foreach (var timer in due) timer.DueAt = null;
            }
            foreach (var timer in due) timer.Fire();
        }
        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                var timer = new ClockTimer(this, callback, state);
                timer.Change(dueTime, period);
                _timers.Add(timer);
                return timer;
            }
        }
        private sealed class ClockTimer(IntegrationClock clock, TimerCallback callback, object? state) : ITimer
        {
            public long? DueAt { get; set; }
            private bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock._gate)
                {
                    if (_disposed) return false;
                    if (period != Timeout.InfiniteTimeSpan) throw new NotSupportedException();
                    DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock._ticks + dueTime.Ticks;
                    return true;
                }
            }
            public void Fire() { if (!_disposed) callback(state); }
            public void Dispose() { lock (clock._gate) { _disposed = true; DueAt = null; } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
