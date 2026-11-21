using System.Data;
using System.Data.Common;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using xKsef.Application.Gus;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Infrastructure.Gus;

public sealed class SqlGusQuotaStore : IGusQuotaStore
{
    public static readonly TimeSpan PermitValidity = TimeSpan.FromMilliseconds(250);
    internal const int CommandTimeoutSeconds = 5;
    internal const int LockTimeoutMilliseconds = 100;
    private readonly TimeProvider _clock;
    private readonly Func<GusRateLimitEnvironment, CancellationToken, Task<AdmissionRow>> _execute;

    public SqlGusQuotaStore(AppDbContext context, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(clock);
        if (!context.Database.IsSqlServer()) throw new InvalidOperationException("Magazyn zgód GUS wymaga SQL Server.");
        // Same configured database, independent connection/transaction. No EF execution strategy retries:
        // a failed commit can have succeeded, and must never be repeated transparently.
        var connectionString = context.Database.GetConnectionString() ?? throw new InvalidOperationException("Brak połączenia SQL Server.");
        _execute = (environment, token) => ExecuteAsync(connectionString, environment, token);
        _clock = clock;
    }

    internal SqlGusQuotaStore(Func<GusRateLimitEnvironment, CancellationToken, Task<AdmissionRow>> execute, TimeProvider clock)
    {
        _execute = execute;
        _clock = clock;
    }

    public async Task<GusQuotaAttempt> TryAcquireAsync(GusRateLimitEnvironment environment, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(environment)) throw new ArgumentOutOfRangeException(nameof(environment));
        cancellationToken.ThrowIfCancellationRequested();
        var started = _clock.GetTimestamp();
        try
        {
            var row = await _execute(environment, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            var grantedAt = new DateTimeOffset(DateTime.SpecifyKind(row.ServerNowUtc, DateTimeKind.Utc));
            if (row.RetryAtUtc is { } retryAt)
            {
                var retryAfter = retryAt - row.ServerNowUtc;
                if (retryAfter <= TimeSpan.Zero) throw new InvalidDataException("Invalid SQL admission result.");
                return GusQuotaAttempt.Denied(retryAfter);
            }
            // Use SQL's UTC anchor and conservatively debit the entire attempt round trip, including
            // opening the connection and commit. Host/SQL wall-clock skew cannot extend the lifetime.
            var permitClock = new AdmissionClock(_clock, started, grantedAt);
            return GusQuotaAttempt.Granted(new GusCallPermit(grantedAt, PermitValidity, permitClock));
        }
        catch (Exception) when (cancellationToken.IsCancellationRequested)
        {
            throw new OperationCanceledException(cancellationToken);
        }
        catch (Exception exception) when (exception is DbException or IOException or InvalidDataException or TimeoutException or InvalidOperationException)
        {
            throw new GusRateLimiterUnavailableException(exception);
        }
    }

    internal static string LockResource(GusRateLimitEnvironment environment) => environment switch
    {
        GusRateLimitEnvironment.Test => "xKsef:GusQuota:Test",
        GusRateLimitEnvironment.Production => "xKsef:GusQuota:Production",
        _ => throw new ArgumentOutOfRangeException(nameof(environment))
    };

    private static async Task<AdmissionRow> ExecuteAsync(string connectionString, GusRateLimitEnvironment environment, CancellationToken token)
    {
        // Suppress ambient enlistment: a later business rollback must not refund a granted permit.
        var target = new SqlConnectionStringBuilder(connectionString) { Enlist = false };
        await using var connection = new SqlConnection(target.ConnectionString);
        await connection.OpenAsync(token);
        await using var transaction = (SqlTransaction)await connection.BeginTransactionAsync(IsolationLevel.ReadCommitted, token);
        // Disposing an uncommitted transaction rolls it back, also after command/cancellation failures.
        using var command = new SqlCommand(AdmissionSql, connection, transaction) { CommandTimeout = CommandTimeoutSeconds };
        command.Parameters.Add("@environment", SqlDbType.TinyInt).Value = (byte)environment;
        command.Parameters.Add("@resource", SqlDbType.NVarChar, 255).Value = LockResource(environment);
        command.Parameters.Add("@lockTimeout", SqlDbType.Int).Value = LockTimeoutMilliseconds;
        AdmissionRow row;
        await using (var reader = await command.ExecuteReaderAsync(token))
        {
            if (!await reader.ReadAsync(token)) throw new InvalidDataException("Missing SQL admission result.");
            row = new AdmissionRow(reader.GetDateTime(0), reader.IsDBNull(1) ? null : reader.GetDateTime(1));
            if (await reader.ReadAsync(token)) throw new InvalidDataException("Unexpected SQL admission result.");
            // Drain the batch before commit: trailing SQL failures must not be ignored.
            if (await reader.NextResultAsync(token)) throw new InvalidDataException("Unexpected SQL result set.");
        }
        await transaction.CommitAsync(token);
        return row;
    }

    internal sealed record AdmissionRow(DateTime ServerNowUtc, DateTime? RetryAtUtc);

    private sealed class AdmissionClock(TimeProvider clock, long started, DateTimeOffset grantedAtUtc) : TimeProvider
    {
        public override long TimestampFrequency => clock.TimestampFrequency;
        public override long GetTimestamp() => clock.GetTimestamp();
        public override DateTimeOffset GetUtcNow() => grantedAtUtc + clock.GetElapsedTime(started);
    }

    internal const string AdmissionSql = """
        SET NOCOUNT ON;
        SET XACT_ABORT ON;
        DECLARE @lockResult int;
        EXEC @lockResult = sys.sp_getapplock @Resource=@resource, @LockMode='Exclusive',
             @LockOwner='Transaction', @LockTimeout=@lockTimeout, @DbPrincipal='public';
        IF @lockResult < 0
            THROW 51000, 'GUS quota application lock was not acquired.', 1;

        DECLARE @now datetime2(7) = SYSUTCDATETIME();
        DECLARE @retryAt datetime2(7);
        SELECT @retryAt = MAX(DATEADD(second, windows.Seconds, recent.Oldest))
        FROM (VALUES (1, 3), (60, 120), (3600, 6000)) AS windows(Seconds, MaxCalls)
        CROSS APPLY (
            SELECT COUNT_BIG(*) AS Calls, MIN(permits.GrantedAtUtc) AS Oldest
            FROM (
                SELECT TOP (windows.MaxCalls) GrantedAtUtc
                FROM [dbo].[GusCallPermits] WITH (READCOMMITTEDLOCK)
                WHERE Environment=@environment AND GrantedAtUtc > DATEADD(second, -windows.Seconds, @now)
                ORDER BY GrantedAtUtc DESC
            ) AS permits
        ) AS recent
        WHERE recent.Calls >= windows.MaxCalls;

        -- The newest MaxCalls entries determine when capacity becomes available even if overfull.
        -- Future-dated entries are counted conservatively if the SQL clock moves backwards.
        IF @retryAt IS NULL
            INSERT INTO [dbo].[GusCallPermits] (Environment, GrantedAtUtc) VALUES (@environment, @now);
        SELECT @now AS ServerNowUtc, @retryAt AS RetryAtUtc;
        """;
}
