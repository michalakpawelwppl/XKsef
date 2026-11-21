namespace xKsef.Application.Gus;

/// <summary>A local single-use permission. Consuming or discarding it never refunds the central quota.</summary>
public sealed class GusCallPermit
{
    private readonly TimeProvider _clock;
    private readonly long _createdAt;
    private readonly TimeSpan _remainingLifetime;
    private int _used;

    /// <summary>
    /// The issuer supplies a short validity and must conservatively account for admission/transport latency
    /// and clock differences when constructing the permit. Subsequent expiry checks are monotonic.
    /// </summary>
    public GusCallPermit(DateTimeOffset grantedAtUtc, TimeSpan validity, TimeProvider? timeProvider = null)
    {
        if (validity <= TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(validity));
        _clock = timeProvider ?? TimeProvider.System;
        GrantedAtUtc = grantedAtUtc.ToUniversalTime();
        ExpiresAtUtc = GrantedAtUtc.Add(validity);
        _createdAt = _clock.GetTimestamp();
        var remaining = ExpiresAtUtc - _clock.GetUtcNow();
        // A future server timestamp must not extend validity beyond the supplied lifetime.
        _remainingLifetime = remaining <= TimeSpan.Zero ? TimeSpan.Zero : remaining < validity ? remaining : validity;
    }

    public DateTimeOffset GrantedAtUtc { get; }
    public DateTimeOffset ExpiresAtUtc { get; }
    public bool IsUsable => Volatile.Read(ref _used) == 0 && !IsExpired;
    private bool IsExpired => _clock.GetElapsedTime(_createdAt) >= _remainingLifetime;

    /// <summary>Atomically consumes the permit immediately before SOAP transmission. It cannot be reused.</summary>
    public bool TryUse()
    {
        if (IsExpired || Interlocked.CompareExchange(ref _used, 1, 0) != 0)
            return false;
        return !IsExpired;
    }
}
