namespace xKsef.Infrastructure.Ksef;

public class KsefRateLimitState
{
    private readonly object _lock = new();
    private DateTimeOffset? _blockedUntil;

    public TimeSpan? GetRemainingDelay()
    {
        lock (_lock)
        {
            if (_blockedUntil is null)
                return null;

            var remaining = _blockedUntil.Value - DateTimeOffset.UtcNow;

            return remaining > TimeSpan.Zero
                ? remaining
                : null;
        }
    }

    public void BlockFor(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
            return;

        lock (_lock)
        {
            var until = DateTimeOffset.UtcNow + delay;

            if (_blockedUntil is null || until > _blockedUntil.Value)
                _blockedUntil = until;
        }
    }
}