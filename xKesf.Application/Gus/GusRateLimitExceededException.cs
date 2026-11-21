namespace xKsef.Application.Gus;

public sealed class GusRateLimitExceededException : Exception
{
    public GusRateLimitExceededException(TimeSpan? retryAfter = null) : base("Wyczerpano budżet oczekiwania na dostęp do GUS BIR.")
    {
        if (retryAfter < TimeSpan.Zero)
            throw new ArgumentOutOfRangeException(nameof(retryAfter));
        RetryAfter = retryAfter;
    }

    public TimeSpan? RetryAfter { get; }
}
