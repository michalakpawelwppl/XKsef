namespace xKsef.Application.Exceptions;

public class ExternalServiceRateLimitException : Exception
{
    public TimeSpan? RetryAfter { get; }

    public ExternalServiceRateLimitException(
        string message,
        TimeSpan? retryAfter = null,
        Exception? innerException = null)
        : base(message, innerException)
    {
        RetryAfter = retryAfter;
    }
}