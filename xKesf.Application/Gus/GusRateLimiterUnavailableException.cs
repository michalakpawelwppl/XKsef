namespace xKsef.Application.Gus;

public sealed class GusRateLimiterUnavailableException(Exception? innerException = null)
    : Exception("Centralny mechanizm limitowania GUS BIR jest niedostępny.", innerException);
