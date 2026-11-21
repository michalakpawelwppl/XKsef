namespace xKsef.Application.Gus;

/// <summary>
/// Cumulative waiting time for one lookup, including its session lock, login, diagnostics and retry.
/// Waiting scopes are sequential and must be disposed before HTTP communication starts.
/// </summary>
public sealed class GusWaitBudget
{
    public static readonly TimeSpan DefaultLimit = TimeSpan.FromSeconds(10);
    private readonly TimeProvider _clock;
    private readonly object _gate = new();
    private TimeSpan _spent;
    private long? _waitingSince;

    public GusWaitBudget(TimeSpan? limit = null, TimeProvider? timeProvider = null)
    {
        Limit = limit ?? DefaultLimit;
        if (Limit <= TimeSpan.Zero || Limit.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentOutOfRangeException(nameof(limit));
        _clock = timeProvider ?? TimeProvider.System;
    }

    public TimeSpan Limit { get; }
    public TimeSpan Remaining { get { lock (_gate) return GetRemaining(); } }
    public bool IsExhausted => Remaining == TimeSpan.Zero;

    public void ThrowIfExhausted(CancellationToken cancellationToken = default, TimeSpan? retryAfter = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (IsExhausted)
            throw new GusRateLimitExceededException(retryAfter);
    }

    /// <summary>
    /// The returned token cancels when the remaining waiting budget expires or the caller cancels.
    /// On OperationCanceledException, call scope.ThrowIfCancellationRequested to distinguish these cases.
    /// Dispose in a finally/using block, including on failure or cancellation. Nested scopes are rejected.
    /// </summary>
    public WaitingScope BeginWaiting(CancellationToken cancellationToken = default, TimeSpan? retryAfter = null)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (_gate)
        {
            if (_waitingSince.HasValue)
                throw new InvalidOperationException("Budżet obsługuje jeden zakres oczekiwania jednocześnie.");
            var remaining = GetRemaining();
            if (remaining == TimeSpan.Zero)
                throw new GusRateLimitExceededException(retryAfter);
            _waitingSince = _clock.GetTimestamp();
            try { return new WaitingScope(this, remaining, cancellationToken, retryAfter); }
            catch { _waitingSince = null; throw; }
        }
    }

    private TimeSpan GetRemaining()
    {
        var elapsed = _waitingSince is { } start ? _clock.GetElapsedTime(start) : TimeSpan.Zero;
        var remaining = Limit - _spent - elapsed;
        return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
    }

    private void EndWaiting()
    {
        lock (_gate)
        {
            _spent = Limit - GetRemaining();
            _waitingSince = null;
        }
    }

    public sealed class WaitingScope : IDisposable
    {
        private readonly GusWaitBudget _budget;
        private readonly CancellationToken _callerToken;
        private readonly TimeSpan? _retryAfter;
        private readonly CancellationTokenSource _deadline;
        private readonly CancellationTokenSource _linked;
        private int _disposed;

        internal WaitingScope(GusWaitBudget budget, TimeSpan remaining, CancellationToken callerToken, TimeSpan? retryAfter)
        {
            if (retryAfter < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(retryAfter));
            _budget = budget;
            _callerToken = callerToken;
            _retryAfter = retryAfter;
            _deadline = new CancellationTokenSource(remaining, budget._clock);
            try { _linked = CancellationTokenSource.CreateLinkedTokenSource(callerToken, _deadline.Token); }
            catch { _deadline.Dispose(); throw; }
        }

        public CancellationToken CancellationToken => _linked.Token;

        public void ThrowIfCancellationRequested()
        {
            _callerToken.ThrowIfCancellationRequested();
            if (_deadline.IsCancellationRequested || _budget.IsExhausted)
                throw new GusRateLimitExceededException(_retryAfter);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _budget.EndWaiting();
            _linked.Dispose();
            _deadline.Dispose();
        }
    }
}
