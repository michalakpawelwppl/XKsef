namespace xKsef.Wpf.Exceptions;

public class KsefRateLimitException : Exception
{
    public TimeSpan? RetryAfter { get; }

    public KsefRateLimitException(TimeSpan? retryAfter)
        : base("Przekroczono limit żądań KSeF.")
    {
        RetryAfter = retryAfter;
    }
}