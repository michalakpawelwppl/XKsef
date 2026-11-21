namespace xKsef.Application.Gus;

public enum GusRateLimitEnvironment
{
    Test,
    Production
}

/// <summary>Grants permission for one actual SOAP call, shared across tenants and API instances.</summary>
public interface IGusRateLimiter
{
    /// <summary>
    /// Returns a single-use permit or throws GusRateLimitExceededException / GusRateLimiterUnavailableException.
    /// Reuse the same budget throughout one lookup. Measure only waiting, including admission attempts and delays,
    /// using budget.BeginWaiting; do not hold a waiting scope during HTTP communication.
    /// Cancellation must propagate as OperationCanceledException. A granted permit is never refunded.
    /// </summary>
    Task<GusCallPermit> AcquireAsync(GusRateLimitEnvironment environment, GusWaitBudget budget, CancellationToken cancellationToken = default);
}
