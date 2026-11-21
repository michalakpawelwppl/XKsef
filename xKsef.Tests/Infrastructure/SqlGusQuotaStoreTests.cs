using System.Data.Common;
using xKsef.Application.Gus;
using xKsef.Infrastructure.Gus;

namespace xKsef.Tests.Infrastructure;

public sealed class SqlGusQuotaStoreTests
{
    private static readonly DateTime ServerNow = new(2026, 11, 21, 12, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task ConfirmedGrantUsesSqlTimeAndDoesNotDependOnHostUtc()
    {
        var clock = new Clock();
        var store = new SqlGusQuotaStore((environment, token) =>
        {
            Assert.Equal(GusRateLimitEnvironment.Production, environment);
            return Task.FromResult(new SqlGusQuotaStore.AdmissionRow(ServerNow, null));
        }, clock);
        var result = await store.TryAcquireAsync(GusRateLimitEnvironment.Production);
        Assert.True(result.IsGranted);
        Assert.Null(result.RetryAfter);
        Assert.Equal(new DateTimeOffset(ServerNow), result.Permit!.GrantedAtUtc);
        Assert.True(result.Permit.TryUse());
        Assert.False(result.Permit.TryUse());
    }

    [Fact]
    public async Task AdmissionRoundTripConsumesPermitValidityAndNeverRefundsOrRetries()
    {
        var clock = new Clock();
        var attempts = 0;
        var store = new SqlGusQuotaStore((_, _) =>
        {
            attempts++;
            clock.Advance(SqlGusQuotaStore.PermitValidity + TimeSpan.FromTicks(1));
            return Task.FromResult(new SqlGusQuotaStore.AdmissionRow(ServerNow, null));
        }, clock);
        var result = await store.TryAcquireAsync(GusRateLimitEnvironment.Test);
        Assert.True(result.IsGranted);
        Assert.False(result.Permit!.TryUse());
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task DenialHasNoPermitAndReturnsServerRelativeRetryAfter()
    {
        var store = new SqlGusQuotaStore((_, _) => Task.FromResult(new SqlGusQuotaStore.AdmissionRow(ServerNow, ServerNow.AddMilliseconds(750))), new Clock());
        var result = await store.TryAcquireAsync(GusRateLimitEnvironment.Test);
        Assert.False(result.IsGranted);
        Assert.Null(result.Permit);
        Assert.Equal(TimeSpan.FromMilliseconds(750), result.RetryAfter);
    }

    [Theory]
    [InlineData("sql")]
    [InlineData("timeout")]
    [InlineData("ambiguous-commit")]
    [InlineData("lock")]
    public async Task StorageFailuresNeverBecomeDenialsOrGrantsAndAreNotRetried(string failure)
    {
        var attempts = 0;
        Exception cause = failure switch
        {
            "sql" => new TestDbException(),
            "timeout" => new TimeoutException(),
            "ambiguous-commit" => new IOException("Commit acknowledgement lost"),
            _ => new InvalidOperationException("Lock not acquired")
        };
        var store = new SqlGusQuotaStore((_, _) => { attempts++; return Task.FromException<SqlGusQuotaStore.AdmissionRow>(cause); }, new Clock());
        var error = await Assert.ThrowsAsync<GusRateLimiterUnavailableException>(() => store.TryAcquireAsync(GusRateLimitEnvironment.Test));
        Assert.Same(cause, error.InnerException);
        Assert.Equal(1, attempts);
    }

    [Fact]
    public async Task AlreadyCanceledAttemptNeverAccessesStorage()
    {
        var store = new SqlGusQuotaStore((_, _) => throw new Xunit.Sdk.XunitException("Storage must not be accessed"), new Clock());
        var token = new CancellationToken(true);
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => store.TryAcquireAsync(GusRateLimitEnvironment.Test, token));
        Assert.Equal(token, error.CancellationToken);
    }

    [Fact]
    public async Task CancellationDuringCommitDiscardsEvenAnOtherwiseSuccessfulResult()
    {
        using var caller = new CancellationTokenSource();
        var store = new SqlGusQuotaStore((_, token) =>
        {
            Assert.Equal(caller.Token, token);
            caller.Cancel();
            return Task.FromResult(new SqlGusQuotaStore.AdmissionRow(ServerNow, null));
        }, new Clock());
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.TryAcquireAsync(GusRateLimitEnvironment.Test, caller.Token));
    }

    [Fact]
    public async Task CanceledSqlFailureRemainsCallerCancellation()
    {
        using var caller = new CancellationTokenSource();
        var store = new SqlGusQuotaStore((_, _) =>
        {
            caller.Cancel();
            return Task.FromException<SqlGusQuotaStore.AdmissionRow>(new TestDbException());
        }, new Clock());
        await Assert.ThrowsAsync<OperationCanceledException>(() => store.TryAcquireAsync(GusRateLimitEnvironment.Test, caller.Token));
    }

    [Fact]
    public async Task InvalidEnvironmentAndMalformedSqlResultFailClosed()
    {
        var calls = 0;
        var store = new SqlGusQuotaStore((_, _) => { calls++; return Task.FromResult(new SqlGusQuotaStore.AdmissionRow(ServerNow, ServerNow)); }, new Clock());
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() => store.TryAcquireAsync((GusRateLimitEnvironment)99));
        Assert.Equal(0, calls);
        await Assert.ThrowsAsync<GusRateLimiterUnavailableException>(() => store.TryAcquireAsync(GusRateLimitEnvironment.Test));
    }

    [Fact]
    public void AttemptResultCannotMixGrantAndDenial()
    {
        Assert.Throws<ArgumentNullException>(() => GusQuotaAttempt.Granted(null!));
        Assert.Throws<ArgumentOutOfRangeException>(() => GusQuotaAttempt.Denied(TimeSpan.Zero));
        Assert.Throws<ArgumentOutOfRangeException>(() => GusQuotaAttempt.Denied(TimeSpan.FromSeconds(-1)));
        Assert.NotEqual(SqlGusQuotaStore.LockResource(GusRateLimitEnvironment.Test), SqlGusQuotaStore.LockResource(GusRateLimitEnvironment.Production));
    }

    private sealed class TestDbException : DbException;
    private sealed class Clock : TimeProvider
    {
        private long _ticks;
        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _ticks;
        public override DateTimeOffset GetUtcNow() => DateTimeOffset.UnixEpoch; // Deliberately unrelated to SQL UTC.
        public void Advance(TimeSpan time) => _ticks += time.Ticks;
    }
}
