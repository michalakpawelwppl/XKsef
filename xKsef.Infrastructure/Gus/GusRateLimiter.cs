using xKsef.Application.Gus;

namespace xKsef.Infrastructure.Gus;

public sealed class GusRateLimiter : IGusRateLimiter
{
    private readonly IGusQuotaStore _store;
    private readonly TimeProvider _clock;
    private readonly Func<TimeSpan> _jitter;

    public GusRateLimiter(IGusQuotaStore store, TimeProvider clock)
        : this(store, clock, () => TimeSpan.FromMilliseconds(Random.Shared.Next(20, 61))) { }

    internal GusRateLimiter(IGusQuotaStore store, TimeProvider clock, Func<TimeSpan> jitter)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(jitter);
        _store = store;
        _clock = clock;
        _jitter = jitter;
    }

    public async Task<GusCallPermit> AcquireAsync(GusRateLimitEnvironment environment, GusWaitBudget budget, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(budget);
        if (!Enum.IsDefined(environment)) throw new ArgumentOutOfRangeException(nameof(environment));
        using var waiting = budget.BeginWaiting(cancellationToken);
        TimeSpan? retryAfter = null;
        var remainingAtDenial = TimeSpan.Zero;

        TimeSpan? RemainingRetryAfter()
        {
            if (retryAfter is not { } suggested) return null;
            // Reuse the budget's monotonic accounting; do not introduce another elapsed-time clock.
            var remaining = suggested - (remainingAtDenial - budget.Remaining);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        try
        {
            while (true)
            {
                waiting.ThrowIfCancellationRequested();
                var attempt = await _store.TryAcquireAsync(environment, waiting.CancellationToken);
                if (attempt.RetryAfter is { } suggested)
                {
                    retryAfter = suggested;
                    remainingAtDenial = budget.Remaining;
                }
                waiting.ThrowIfCancellationRequested();
                if (attempt.Permit is { } permit)
                    return permit; // Do not consume it: TryUse belongs immediately before SOAP transmission.

                var remaining = budget.Remaining;
                var delay = retryAfter!.Value >= remaining ? remaining : retryAfter.Value;
                // Add only positive jitter that fits. Avoid overflow for very large RetryAfter values.
                var room = remaining - delay;
                var jitter = _jitter();
                if (jitter <= TimeSpan.Zero) throw new InvalidOperationException("Jitter musi być dodatni.");
                delay += jitter < room ? jitter : room;
                // Task.Delay rounds sub-millisecond durations down to zero. At the budget's very end,
                // wait for its cancellation instead of repeatedly querying SQL without any delay.
                await Task.Delay(delay < TimeSpan.FromMilliseconds(1) ? Timeout.InfiniteTimeSpan : delay, _clock, waiting.CancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (waiting.CancellationToken.IsCancellationRequested || budget.IsExhausted)
                throw new GusRateLimitExceededException(RemainingRetryAfter());
            throw;
        }
        catch (GusRateLimitExceededException)
        {
            cancellationToken.ThrowIfCancellationRequested();
            throw new GusRateLimitExceededException(RemainingRetryAfter());
        }
        // Storage unavailability and technical errors propagate unchanged and are never retried.
    }
}
