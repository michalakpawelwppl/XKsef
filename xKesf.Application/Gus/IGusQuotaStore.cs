namespace xKsef.Application.Gus;

/// <summary>One atomic admission attempt, with no retry, queue or waiting loop.</summary>
public interface IGusQuotaStore
{
    /// <summary>
    /// A grant is returned only after confirmed commit. The permit may already have expired in transit;
    /// the sender must successfully TryUse it before transmission. Storage/lock failures throw
    /// GusRateLimiterUnavailableException; caller cancellation remains OperationCanceledException.
    /// </summary>
    Task<GusQuotaAttempt> TryAcquireAsync(GusRateLimitEnvironment environment, CancellationToken cancellationToken = default);
}

public sealed class GusQuotaAttempt
{
    private GusQuotaAttempt(GusCallPermit? permit, TimeSpan? retryAfter) { Permit = permit; RetryAfter = retryAfter; }

    public GusCallPermit? Permit { get; }
    public bool IsGranted => Permit is not null;
    public TimeSpan? RetryAfter { get; }

    public static GusQuotaAttempt Granted(GusCallPermit permit)
    {
        ArgumentNullException.ThrowIfNull(permit);
        return new GusQuotaAttempt(permit, null);
    }

    public static GusQuotaAttempt Denied(TimeSpan retryAfter)
    {
        if (retryAfter <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(retryAfter));
        return new GusQuotaAttempt(null, retryAfter);
    }
}
