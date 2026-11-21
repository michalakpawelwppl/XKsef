using xKsef.Application.Gus;
using xKsef.Infrastructure.Gus;

namespace xKsef.Tests.Infrastructure;

public sealed class GusRateLimiterTests
{
    private static readonly TimeSpan Jitter = TimeSpan.FromMilliseconds(20);

    [Fact]
    public async Task ImmediateGrantIsReturnedUnconsumedForRequestedEnvironment()
    {
        var clock = new Clock();
        var permit = Permit(clock);
        var store = new Store((environment, _) =>
        {
            Assert.Equal(GusRateLimitEnvironment.Production, environment);
            return Task.FromResult(GusQuotaAttempt.Granted(permit));
        });
        var budget = Budget(clock);
        Assert.Same(permit, await Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Production, budget));
        Assert.True(permit.TryUse());
        Assert.Equal(TimeSpan.FromSeconds(10), budget.Remaining);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task DenialWaitsForSuggestionPlusPositiveJitterThenReturnsGrant()
    {
        var clock = new Clock();
        var store = new Store((_, _) => Task.FromResult(clock.Elapsed == TimeSpan.Zero ? GusQuotaAttempt.Denied(TimeSpan.FromSeconds(1)) : GusQuotaAttempt.Granted(Permit(clock))));
        var budget = Budget(clock);
        var pending = Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, budget);
        Assert.False(pending.IsCompleted);
        Assert.Equal(1, store.Calls);
        Assert.Equal(TimeSpan.FromSeconds(1) + Jitter, clock.Duration(2));
        clock.Advance(TimeSpan.FromSeconds(1) + Jitter);
        Assert.NotNull(await pending);
        Assert.Equal(2, store.Calls);
        Assert.Equal(TimeSpan.FromSeconds(9) - Jitter, budget.Remaining);
    }

    [Fact]
    public async Task MultipleDenialsAccumulateWaitingInsteadOfResettingBudget()
    {
        var clock = new Clock();
        var responses = new Queue<GusQuotaAttempt>([GusQuotaAttempt.Denied(TimeSpan.FromSeconds(1)), GusQuotaAttempt.Denied(TimeSpan.FromSeconds(2))]);
        var store = new Store((_, _) => Task.FromResult(responses.TryDequeue(out var response) ? response : GusQuotaAttempt.Granted(Permit(clock))));
        var budget = Budget(clock);
        var pending = Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, budget);
        clock.Advance(TimeSpan.FromSeconds(1) + Jitter);
        await clock.WaitForTimerAsync(3);
        Assert.Equal(2, store.Calls);
        clock.Advance(TimeSpan.FromSeconds(2) + Jitter);
        await pending;
        Assert.Equal(3, store.Calls);
        Assert.Equal(TimeSpan.FromSeconds(7) - Jitter - Jitter, budget.Remaining);
    }

    [Theory]
    [InlineData(60, 50)]
    [InlineData(10, 0)]
    [InlineData(11, 1)]
    public async Task ExhaustionClampsDelayAndReturnsRemainingRetryAfter(int suggestedSeconds, int expectedRetrySeconds)
    {
        var clock = new Clock();
        var store = new Store((_, _) => Task.FromResult(GusQuotaAttempt.Denied(TimeSpan.FromSeconds(suggestedSeconds))));
        var budget = Budget(clock);
        var pending = Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, budget);
        Assert.Equal(TimeSpan.FromSeconds(10), clock.Duration(2));
        clock.Advance(TimeSpan.FromSeconds(10));
        var error = await Assert.ThrowsAsync<GusRateLimitExceededException>(() => pending);
        Assert.Equal(TimeSpan.FromSeconds(expectedRetrySeconds), error.RetryAfter);
        Assert.Equal(1, store.Calls);
        Assert.True(budget.IsExhausted);
    }

    [Fact]
    public async Task LatestDenialReplacesPreviousRetrySuggestion()
    {
        var clock = new Clock();
        var store = new Store((_, _) => Task.FromResult(GusQuotaAttempt.Denied(clock.Elapsed == TimeSpan.Zero ? TimeSpan.FromSeconds(1) : TimeSpan.FromSeconds(30))));
        var budget = Budget(clock);
        var pending = Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, budget);
        clock.Advance(TimeSpan.FromSeconds(1) + Jitter);
        await clock.WaitForTimerAsync(3);
        clock.Advance(budget.Remaining);
        var error = await Assert.ThrowsAsync<GusRateLimitExceededException>(() => pending);
        Assert.Equal(TimeSpan.FromSeconds(21) + Jitter, error.RetryAfter);
        Assert.Equal(2, store.Calls);
    }

    [Fact]
    public async Task CancellationWhileDelayingPropagatesCallerTokenWithoutRetry()
    {
        var clock = new Clock();
        using var caller = new CancellationTokenSource();
        var store = new Store((_, _) => Task.FromResult(GusQuotaAttempt.Denied(TimeSpan.FromSeconds(5))));
        var budget = Budget(clock);
        var pending = Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, budget, caller.Token);
        clock.Advance(TimeSpan.FromSeconds(2));
        caller.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Equal(1, store.Calls);
        Assert.Equal(TimeSpan.FromSeconds(8), budget.Remaining);
    }

    [Fact]
    public async Task CancellationInterruptsSqlAttempt()
    {
        var clock = new Clock();
        using var caller = new CancellationTokenSource();
        var store = BlockingStore();
        var pending = Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, Budget(clock), caller.Token);
        caller.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => pending);
        Assert.Equal(caller.Token, error.CancellationToken);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task BudgetDeadlineInterruptsSqlAttemptWithUnknownRetryAfter()
    {
        var clock = new Clock();
        var store = BlockingStore();
        var budget = Budget(clock);
        var pending = Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, budget);
        clock.Advance(TimeSpan.FromSeconds(10));
        var error = await Assert.ThrowsAsync<GusRateLimitExceededException>(() => pending);
        Assert.Null(error.RetryAfter);
        Assert.Equal(1, store.Calls);
        Assert.True(budget.IsExhausted);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task StorageAndTechnicalFailuresPropagateUnchangedWithoutRetry(bool unavailable)
    {
        var clock = new Clock();
        Exception cause = unavailable ? new GusRateLimiterUnavailableException() : new InvalidOperationException("Technical failure");
        var store = new Store((_, _) => Task.FromException<GusQuotaAttempt>(cause));
        var error = await Record.ExceptionAsync(() => Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, Budget(clock)));
        Assert.Same(cause, error);
        Assert.Equal(1, store.Calls);
        Assert.Equal(1, clock.TimerCount); // Only the waiting deadline, no retry delay.
    }

    [Fact]
    public async Task SqlFailureAfterDenialIsNotRetried()
    {
        var clock = new Clock();
        var cause = new GusRateLimiterUnavailableException();
        var store = new Store((_, _) => clock.Elapsed == TimeSpan.Zero
            ? Task.FromResult(GusQuotaAttempt.Denied(TimeSpan.FromSeconds(1))) : Task.FromException<GusQuotaAttempt>(cause));
        var pending = Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, Budget(clock));
        clock.Advance(TimeSpan.FromSeconds(1) + Jitter);
        Assert.Same(cause, await Record.ExceptionAsync(() => pending));
        Assert.Equal(2, store.Calls);
    }

    [Fact]
    public async Task SharedBudgetIncludesSessionGateAndSqlButExcludesHttpBetweenAcquisitions()
    {
        var clock = new Clock();
        var budget = Budget(clock);
        using (budget.BeginWaiting()) clock.Advance(TimeSpan.FromSeconds(3)); // Session gate.
        var store = new Store((_, _) =>
        {
            clock.Advance(TimeSpan.FromSeconds(2)); // SQL/lock waiting.
            return Task.FromResult(GusQuotaAttempt.Granted(Permit(clock)));
        });
        var limiter = Limiter(store, clock);
        await limiter.AcquireAsync(GusRateLimitEnvironment.Test, budget);
        clock.Advance(TimeSpan.FromMinutes(2)); // HTTP outside limiter's waiting scope.
        Assert.Equal(TimeSpan.FromSeconds(5), budget.Remaining);
        await limiter.AcquireAsync(GusRateLimitEnvironment.Test, budget);
        Assert.Equal(TimeSpan.FromSeconds(3), budget.Remaining);
        var deny = new Store((_, _) => Task.FromResult(GusQuotaAttempt.Denied(TimeSpan.FromSeconds(8))));
        var pending = Limiter(deny, clock).AcquireAsync(GusRateLimitEnvironment.Test, budget);
        clock.Advance(TimeSpan.FromSeconds(3));
        var error = await Assert.ThrowsAsync<GusRateLimitExceededException>(() => pending);
        Assert.Equal(TimeSpan.FromSeconds(5), error.RetryAfter);
        Assert.True(budget.IsExhausted);
    }

    [Fact]
    public async Task SqlResultArrivingAfterDeadlineIsNotReturnedAsGrant()
    {
        var clock = new Clock();
        var store = new Store((_, _) =>
        {
            clock.Advance(TimeSpan.FromSeconds(11));
            return Task.FromResult(GusQuotaAttempt.Granted(Permit(clock)));
        });
        await Assert.ThrowsAsync<GusRateLimitExceededException>(() => Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, Budget(clock)));
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task TinySuggestionStillWaitsForJitterAndHugeSuggestionCannotOverflow()
    {
        var clock = new Clock();
        using var caller = new CancellationTokenSource();
        var store = new Store((_, _) => Task.FromResult(GusQuotaAttempt.Denied(TimeSpan.FromTicks(1))));
        var pending = Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, Budget(clock), caller.Token);
        Assert.InRange(clock.Duration(2), Jitter, Jitter + TimeSpan.FromTicks(1));
        caller.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => pending);

        clock = new Clock();
        store = new Store((_, _) => Task.FromResult(GusQuotaAttempt.Denied(TimeSpan.MaxValue)));
        pending = Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, Budget(clock));
        clock.Advance(TimeSpan.FromSeconds(10));
        var error = await Assert.ThrowsAsync<GusRateLimitExceededException>(() => pending);
        Assert.Equal(TimeSpan.MaxValue - TimeSpan.FromSeconds(10), error.RetryAfter);
    }

    [Fact]
    public async Task ExhaustedBudgetAndPreCanceledCallerNeverReachStore()
    {
        var clock = new Clock();
        var store = new Store((_, _) => throw new InvalidOperationException("Must not access storage"));
        var limiter = Limiter(store, clock);
        var budget = Budget(clock);
        using (budget.BeginWaiting()) clock.Advance(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAsync<GusRateLimitExceededException>(() => limiter.AcquireAsync(GusRateLimitEnvironment.Test, budget));
        await Assert.ThrowsAsync<OperationCanceledException>(() => limiter.AcquireAsync(GusRateLimitEnvironment.Test, Budget(clock), new CancellationToken(true)));
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => limiter.AcquireAsync((GusRateLimitEnvironment)42, Budget(clock)));
        await Assert.ThrowsAsync<ArgumentNullException>(() => limiter.AcquireAsync(GusRateLimitEnvironment.Test, null!));
        Assert.Equal(0, store.Calls);
    }

    [Fact]
    public async Task SubMillisecondRemainderWaitsForBudgetDeadlineWithoutPollingSql()
    {
        var clock = new Clock();
        var budget = new GusWaitBudget(TimeSpan.FromMilliseconds(1), clock);
        using (budget.BeginWaiting()) clock.Advance(TimeSpan.FromTicks(5000));
        var store = new Store((_, _) => Task.FromResult(GusQuotaAttempt.Denied(TimeSpan.FromSeconds(1))));
        var pending = Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, budget);
        Assert.False(pending.IsCompleted);
        Assert.Equal(1, store.Calls);
        clock.Advance(TimeSpan.FromTicks(5000));
        await Assert.ThrowsAsync<GusRateLimitExceededException>(() => pending);
        Assert.Equal(1, store.Calls);
    }

    [Fact]
    public async Task DenialArrivingAtDeadlineStillProvidesLatestRetryAfter()
    {
        var clock = new Clock();
        var store = new Store((_, _) =>
        {
            clock.Advance(TimeSpan.FromSeconds(10));
            return Task.FromResult(GusQuotaAttempt.Denied(TimeSpan.FromSeconds(30)));
        });
        var error = await Assert.ThrowsAsync<GusRateLimitExceededException>(() => Limiter(store, clock).AcquireAsync(GusRateLimitEnvironment.Test, Budget(clock)));
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
        Assert.Equal(1, store.Calls);
    }

    private static GusRateLimiter Limiter(Store store, Clock clock) => new(store, clock, () => Jitter);
    private static GusWaitBudget Budget(Clock clock) => new(timeProvider: clock);
    private static GusCallPermit Permit(Clock clock) => new(clock.GetUtcNow(), TimeSpan.FromSeconds(1), clock);
    private static Store BlockingStore() => new(async (_, token) =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
        throw new InvalidOperationException("Unreachable");
    });

    private sealed class Store(Func<GusRateLimitEnvironment, CancellationToken, Task<GusQuotaAttempt>> attempt) : IGusQuotaStore
    {
        public int Calls { get; private set; }
        public Task<GusQuotaAttempt> TryAcquireAsync(GusRateLimitEnvironment environment, CancellationToken cancellationToken = default)
        {
            Calls++;
            return attempt(environment, cancellationToken);
        }
    }

    private sealed class Clock : TimeProvider
    {
        private readonly object _gate = new();
        private readonly List<Timer> _timers = [];
        private readonly List<(int Count, TaskCompletionSource Completion)> _observers = [];
        private long _ticks;
        public TimeSpan Elapsed => TimeSpan.FromTicks(GetTimestamp());
        public int TimerCount { get { lock (_gate) return _timers.Count; } }
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() { lock (_gate) return _ticks; }
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch + Elapsed;
        public TimeSpan Duration(int timerNumber) { lock (_gate) return _timers[timerNumber - 1].Duration; }

        public Task WaitForTimerAsync(int count)
        {
            lock (_gate)
            {
                if (_timers.Count >= count) return Task.CompletedTask;
                var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                _observers.Add((count, completion));
                return completion.Task.WaitAsync(TimeSpan.FromSeconds(5)); // Guard against a broken test, not simulated waiting.
            }
        }

        public void Advance(TimeSpan time)
        {
            TimerCallback[] callbacks;
            object?[] states;
            lock (_gate)
            {
                _ticks += time.Ticks;
                var due = _timers.Where(t => t.DueAt is { } at && at <= _ticks).ToArray();
                foreach (var timer in due) timer.DueAt = null;
                callbacks = due.Select(t => t.Callback).ToArray();
                states = due.Select(t => t.State).ToArray();
            }
            for (var i = 0; i < callbacks.Length; i++) callbacks[i](states[i]);
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            lock (_gate)
            {
                var timer = new Timer(this, callback, state);
                timer.Change(dueTime, period);
                _timers.Add(timer);
                foreach (var observer in _observers.Where(o => o.Count <= _timers.Count).ToArray())
                {
                    observer.Completion.TrySetResult();
                    _observers.Remove(observer);
                }
                return timer;
            }
        }

        private sealed class Timer(Clock clock, TimerCallback callback, object? state) : ITimer
        {
            public TimerCallback Callback { get; } = callback;
            public object? State { get; } = state;
            public long? DueAt { get; set; }
            public TimeSpan Duration { get; private set; }
            private bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                lock (clock._gate)
                {
                    if (_disposed) return false;
                    if (period != Timeout.InfiniteTimeSpan) throw new NotSupportedException();
                    Duration = dueTime;
                    DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock._ticks + dueTime.Ticks;
                    return true;
                }
            }
            public void Dispose() { lock (clock._gate) { _disposed = true; DueAt = null; } }
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
