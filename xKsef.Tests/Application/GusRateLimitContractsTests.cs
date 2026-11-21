using xKsef.Application.Gus;

namespace xKsef.Tests.Application;

public sealed class GusRateLimitContractsTests
{
    [Fact]
    public void DefaultBudgetIsTenSecondsAndDoesNotChargeOutsideWaitingScopes()
    {
        var clock = new ManualClock();
        var budget = new GusWaitBudget(timeProvider: clock);
        clock.Advance(TimeSpan.FromHours(1));
        Assert.Equal(TimeSpan.FromSeconds(10), budget.Remaining);
        Assert.False(budget.IsExhausted);
    }

    [Fact]
    public void SessionLoginSearchDiagnosticsAndRetryShareOneBudgetButHttpDoesNotConsumeIt()
    {
        var clock = new ManualClock();
        var budget = new GusWaitBudget(timeProvider: clock);
        // Session gate, login admission, search admission, diagnostics admission, retry admission.
        foreach (var seconds in new[] { 2, 1, 2, 1, 3 })
        {
            using (budget.BeginWaiting()) clock.Advance(TimeSpan.FromSeconds(seconds));
            clock.Advance(TimeSpan.FromSeconds(30)); // Simulated HTTP, outside waiting scope.
        }
        Assert.Equal(TimeSpan.FromSeconds(1), budget.Remaining);
        using (budget.BeginWaiting()) clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(budget.IsExhausted);
        Assert.Throws<GusRateLimitExceededException>(() => budget.BeginWaiting());
    }

    [Fact]
    public void RemainingTimeIsLiveAndUnaffectedByWallClockChanges()
    {
        var clock = new ManualClock();
        var budget = new GusWaitBudget(timeProvider: clock);
        using var waiting = budget.BeginWaiting();
        clock.Advance(TimeSpan.FromSeconds(3));
        clock.ShiftUtc(TimeSpan.FromDays(-7));
        Assert.Equal(TimeSpan.FromSeconds(7), budget.Remaining);
        clock.ShiftUtc(TimeSpan.FromDays(14));
        Assert.Equal(TimeSpan.FromSeconds(7), budget.Remaining);
    }

    [Fact]
    public async Task DeadlineCancelsPendingWaitAndProvidesRetryAfter()
    {
        var clock = new ManualClock();
        var budget = new GusWaitBudget(timeProvider: clock);
        using var waiting = budget.BeginWaiting(retryAfter: TimeSpan.FromSeconds(4));
        var pending = Task.Delay(Timeout.InfiniteTimeSpan, waiting.CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pending);
        var error = Assert.Throws<GusRateLimitExceededException>(waiting.ThrowIfCancellationRequested);
        Assert.Equal(TimeSpan.FromSeconds(4), error.RetryAfter);
        Assert.True(budget.IsExhausted);
    }

    [Fact]
    public void NextWaitingScopeUsesOnlyRemainingDeadline()
    {
        var clock = new ManualClock();
        var budget = new GusWaitBudget(timeProvider: clock);
        using (budget.BeginWaiting()) clock.Advance(TimeSpan.FromSeconds(7));
        clock.Advance(TimeSpan.FromMinutes(1)); // Time outside waiting does not reset or consume the budget.
        using var next = budget.BeginWaiting();
        clock.Advance(TimeSpan.FromSeconds(2));
        Assert.False(next.CancellationToken.IsCancellationRequested);
        clock.Advance(TimeSpan.FromSeconds(1));
        Assert.True(next.CancellationToken.IsCancellationRequested);
        Assert.Throws<GusRateLimitExceededException>(next.ThrowIfCancellationRequested);
    }

    [Fact]
    public void CallerCancellationRemainsDistinctFromBudgetExpiryAndHasPriority()
    {
        var clock = new ManualClock();
        var budget = new GusWaitBudget(timeProvider: clock);
        using var caller = new CancellationTokenSource();
        using var waiting = budget.BeginWaiting(caller.Token);
        clock.Advance(TimeSpan.FromSeconds(2));
        caller.Cancel();
        Assert.True(waiting.CancellationToken.IsCancellationRequested);
        Assert.Equal(caller.Token, Assert.Throws<OperationCanceledException>(waiting.ThrowIfCancellationRequested).CancellationToken);
        clock.Advance(TimeSpan.FromSeconds(8));
        Assert.Throws<OperationCanceledException>(waiting.ThrowIfCancellationRequested);
    }

    [Fact]
    public void AlreadyCanceledOperationDoesNotStartWaiting()
    {
        var budget = new GusWaitBudget();
        var token = new CancellationToken(true);
        Assert.Throws<OperationCanceledException>(() => budget.BeginWaiting(token));
        Assert.Throws<OperationCanceledException>(() => budget.ThrowIfExhausted(token));
        Assert.Equal(budget.Limit, budget.Remaining);
    }

    [Fact]
    public void DisposingAfterFailureChargesElapsedTimeAndStopsItsDeadline()
    {
        var clock = new ManualClock();
        var budget = new GusWaitBudget(timeProvider: clock);
        var waiting = budget.BeginWaiting();
        var token = waiting.CancellationToken;
        clock.Advance(TimeSpan.FromSeconds(2));
        waiting.Dispose();
        waiting.Dispose();
        clock.Advance(TimeSpan.FromMinutes(1));
        Assert.False(token.IsCancellationRequested);
        Assert.Equal(TimeSpan.FromSeconds(8), budget.Remaining);
        using var next = budget.BeginWaiting();
        next.ThrowIfCancellationRequested();
    }

    [Fact]
    public void NestedScopesAreRejectedWithoutCorruptingBudget()
    {
        var clock = new ManualClock();
        var budget = new GusWaitBudget(timeProvider: clock);
        using (budget.BeginWaiting())
        {
            Assert.Throws<InvalidOperationException>(() => budget.BeginWaiting());
            clock.Advance(TimeSpan.FromSeconds(2));
        }
        Assert.Equal(TimeSpan.FromSeconds(8), budget.Remaining);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(4294967295)]
    public void InvalidBudgetLimitsAreRejected(long milliseconds)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new GusWaitBudget(TimeSpan.FromMilliseconds(milliseconds)));

    [Fact]
    public void ExpiredBudgetIsClampedToZeroAndCanProvideRetryAfter()
    {
        var clock = new ManualClock();
        var budget = new GusWaitBudget(TimeSpan.FromSeconds(2), clock);
        using (budget.BeginWaiting()) clock.Advance(TimeSpan.FromMinutes(1));
        Assert.Equal(TimeSpan.Zero, budget.Remaining);
        Assert.Equal(TimeSpan.FromSeconds(7), Assert.Throws<GusRateLimitExceededException>(() => budget.ThrowIfExhausted(retryAfter: TimeSpan.FromSeconds(7))).RetryAfter);
    }

    [Fact]
    public void InvalidRetryAfterDoesNotLeaveActiveWaitingScope()
    {
        var budget = new GusWaitBudget();
        Assert.Throws<ArgumentOutOfRangeException>(() => budget.BeginWaiting(retryAfter: TimeSpan.FromSeconds(-1)));
        using var waiting = budget.BeginWaiting();
        waiting.ThrowIfCancellationRequested();
    }

    [Fact]
    public void PermitHasUtcDatesAndCanOnlyBeUsedOnce()
    {
        var clock = new ManualClock();
        var granted = clock.GetUtcNow().ToOffset(TimeSpan.FromHours(1));
        var permit = new GusCallPermit(granted, TimeSpan.FromMilliseconds(100), clock);
        Assert.Equal(TimeSpan.Zero, permit.GrantedAtUtc.Offset);
        Assert.Equal(granted.AddMilliseconds(100), permit.ExpiresAtUtc);
        Assert.True(permit.IsUsable);
        Assert.True(permit.TryUse());
        Assert.False(permit.IsUsable);
        Assert.False(permit.TryUse());
    }

    [Fact]
    public void PermitExpiresAtBoundaryEvenWhenUtcClockMovesBackwards()
    {
        var clock = new ManualClock();
        var permit = new GusCallPermit(clock.GetUtcNow(), TimeSpan.FromMilliseconds(100), clock);
        clock.ShiftUtc(TimeSpan.FromDays(-1));
        clock.Advance(TimeSpan.FromMilliseconds(100));
        Assert.False(permit.IsUsable);
        Assert.False(permit.TryUse());
    }

    [Fact]
    public void OldPermitIsAlreadyExpiredAndFutureTimestampCannotExtendValidity()
    {
        var clock = new ManualClock();
        var validity = TimeSpan.FromMilliseconds(100);
        var old = new GusCallPermit(clock.GetUtcNow() - validity, validity, clock);
        var future = new GusCallPermit(clock.GetUtcNow().AddHours(1), validity, clock);
        Assert.False(old.TryUse());
        clock.Advance(validity);
        Assert.False(future.TryUse());
    }

    [Fact]
    public void PermitAccountsForAgeAtConstruction()
    {
        var clock = new ManualClock();
        var permit = new GusCallPermit(clock.GetUtcNow().AddMilliseconds(-70), TimeSpan.FromMilliseconds(100), clock);
        clock.Advance(TimeSpan.FromMilliseconds(29));
        Assert.True(permit.IsUsable);
        clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.False(permit.IsUsable);
    }

    [Fact]
    public async Task OnlyOneConcurrentConsumerCanUsePermit()
    {
        var clock = new ManualClock();
        var permit = new GusCallPermit(clock.GetUtcNow(), TimeSpan.FromSeconds(1), clock);
        var attempts = await Task.WhenAll(Enumerable.Range(0, 100).Select(_ => Task.Run(permit.TryUse)));
        Assert.Single(attempts, granted => granted);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void NonPositivePermitValidityIsRejected(int milliseconds)
        => Assert.Throws<ArgumentOutOfRangeException>(() => new GusCallPermit(DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(milliseconds)));

    [Fact]
    public void ErrorsDistinguishExhaustionFromUnavailableLimiterWithoutHttpDependencies()
    {
        Assert.Null(new GusRateLimitExceededException().RetryAfter);
        Assert.Throws<ArgumentOutOfRangeException>(() => new GusRateLimitExceededException(TimeSpan.FromSeconds(-1)));
        var cause = new InvalidOperationException("test");
        var unavailable = new GusRateLimiterUnavailableException(cause);
        Assert.Same(cause, unavailable.InnerException);
        Assert.IsNotType<GusRateLimitExceededException>(unavailable);
        Assert.DoesNotContain(cause.Message, unavailable.Message);
    }

    private sealed class ManualClock : TimeProvider
    {
        private DateTimeOffset _utc = new(2026, 11, 21, 12, 0, 0, TimeSpan.Zero);
        private long _timestamp;
        private readonly List<ManualTimer> _timers = [];
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;
        public override DateTimeOffset GetUtcNow() => _utc;
        public void ShiftUtc(TimeSpan delta) => _utc += delta;
        public void Advance(TimeSpan delta)
        {
            _timestamp += delta.Ticks;
            _utc += delta;
            foreach (var timer in _timers.ToArray()) timer.FireIfDue();
        }

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new ManualTimer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        private sealed class ManualTimer(ManualClock clock, TimerCallback callback, object? state) : ITimer
        {
            private long? _dueAt;
            private bool _disposed;
            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                if (_disposed) return false;
                if (period != Timeout.InfiniteTimeSpan) throw new NotSupportedException("Only one-shot timers are used in these tests.");
                _dueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock.GetTimestamp() + dueTime.Ticks;
                return true;
            }
            public void FireIfDue()
            {
                if (!_disposed && _dueAt is { } due && clock.GetTimestamp() >= due)
                {
                    _dueAt = null;
                    callback(state);
                }
            }
            public void Dispose() => _disposed = true;
            public ValueTask DisposeAsync() { Dispose(); return ValueTask.CompletedTask; }
        }
    }
}
