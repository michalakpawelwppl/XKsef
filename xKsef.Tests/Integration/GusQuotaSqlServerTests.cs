using System.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using xKsef.Application.Gus;
using xKsef.Infrastructure.Gus;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

/// <summary>Opt-in SQL only. Fixture owns a random LocalDB database and never reads application settings.</summary>
[Trait("Category", "ManualGusQuotaSql")]
public sealed class GusQuotaSqlServerTests(GusQuotaSqlFixture fixture) : IClassFixture<GusQuotaSqlFixture>, IAsyncLifetime
{
    private SqlServerTestDatabase Database => fixture.Database!;

    public async Task InitializeAsync()
    {
        if (fixture.Database is null) return;
        await using var context = Database.CreateContext();
        await context.Database.ExecuteSqlRawAsync("DELETE FROM [dbo].[GusCallPermits]");
    }
    public Task DisposeAsync() => Task.CompletedTask;

    [ManualGusQuotaSqlFact]
    public async Task AppliesAllMigrationsAndGrantsExactlyOnePermit()
    {
        await using var context = Database.CreateContext();
        Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        Assert.Equal(context.Database.GetMigrations(), await context.Database.GetAppliedMigrationsAsync());
        Assert.False(context.Database.HasPendingModelChanges());
        var result = await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test);
        Assert.True(result.IsGranted);
        Assert.Null(result.RetryAfter);
        var entry = Assert.Single(await context.GusCallPermits.ToArrayAsync());
        Assert.Equal(GusRateLimitEnvironment.Test, entry.Environment);
        Assert.Equal(entry.GrantedAtUtc, result.Permit!.GrantedAtUtc.UtcDateTime);
        Assert.Equal(SqlGusQuotaStore.PermitValidity, result.Permit.ExpiresAtUtc - result.Permit.GrantedAtUtc);
        await Assert.ThrowsAsync<SqlException>(() => ExecuteAsync("INSERT INTO dbo.GusCallPermits(Environment,GrantedAtUtc) VALUES(2,SYSUTCDATETIME())"));
    }

    [ManualGusQuotaSqlFact]
    public async Task SecondWindowDeniesThenReleasesCapacity()
    {
        await SeedAsync(3, 0);
        await using var context = Database.CreateContext();
        var denied = await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test);
        Assert.False(denied.IsGranted);
        Assert.InRange(denied.RetryAfter!.Value, TimeSpan.FromTicks(1), TimeSpan.FromSeconds(1));
        Assert.Equal(3, await context.GusCallPermits.CountAsync());
        await Task.Delay(denied.RetryAfter.Value + TimeSpan.FromMilliseconds(50));
        Assert.True((await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test)).IsGranted);
        Assert.Equal(4, await context.GusCallPermits.CountAsync());
    }

    [ManualGusQuotaSqlFact]
    public async Task MinuteWindowDeniesAndUsesTheCorrectExpiryEvenWhenOverfull()
    {
        await SeedAsync(5, 20);
        await SeedAsync(120, 2);
        await using var context = Database.CreateContext();
        var denied = await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test);
        Assert.False(denied.IsGranted);
        Assert.InRange(denied.RetryAfter!.Value.TotalSeconds, 50, 58);
        Assert.Equal(125, await context.GusCallPermits.CountAsync());
        await ExecuteAsync("UPDATE dbo.GusCallPermits SET GrantedAtUtc=DATEADD(second,-61,SYSUTCDATETIME())");
        Assert.True((await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test)).IsGranted);
    }

    [ManualGusQuotaSqlFact]
    public async Task HourWindowDominatesShorterWindowsAndExpiresWithoutDeletingRows()
    {
        await SeedAsync(6000, 61);
        await SeedAsync(120, 2);
        await SeedAsync(3, 0);
        await using var context = Database.CreateContext();
        var denied = await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test);
        Assert.False(denied.IsGranted);
        Assert.InRange(denied.RetryAfter!.Value.TotalSeconds, 3500, 3539);
        Assert.Equal(6123, await context.GusCallPermits.CountAsync());
        await ExecuteAsync("UPDATE dbo.GusCallPermits SET GrantedAtUtc=DATEADD(second,-3601,SYSUTCDATETIME())");
        Assert.True((await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test)).IsGranted);
        Assert.Equal(6124, await context.GusCallPermits.CountAsync());
    }

    [ManualGusQuotaSqlFact]
    public async Task TestAndProductionHaveIndependentQuotas()
    {
        await SeedAsync(3, 0);
        await using var context = Database.CreateContext();
        Assert.False((await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test)).IsGranted);
        Assert.True((await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Production)).IsGranted);
    }

    [ManualGusQuotaSqlFact]
    public async Task MissingApplicationLockIsUnavailableAndDoesNotInsert()
    {
        await using var holder = new SqlConnection(Database.InstanceConnectionString);
        await holder.OpenAsync();
        await using var transaction = (SqlTransaction)await holder.BeginTransactionAsync();
        using var command = new SqlCommand("DECLARE @r int; EXEC @r=sys.sp_getapplock @Resource=@resource,@LockMode='Exclusive',@LockOwner='Transaction',@LockTimeout=0; SELECT @r;", holder, transaction);
        command.Parameters.AddWithValue("@resource", SqlGusQuotaStore.LockResource(GusRateLimitEnvironment.Test));
        Assert.True(Convert.ToInt32(await command.ExecuteScalarAsync()) >= 0);
        await using var context = Database.CreateContext();
        var error = await Assert.ThrowsAsync<GusRateLimiterUnavailableException>(() => Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test));
        Assert.Equal(51000, Assert.IsType<SqlException>(error.InnerException).Number);
        Assert.Empty(await context.GusCallPermits.ToArrayAsync());
        // A lock in TEST does not block Production.
        Assert.True((await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Production)).IsGranted);
        await transaction.RollbackAsync();
        Assert.True((await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test)).IsGranted);
    }

    [ManualGusQuotaSqlFact]
    public async Task CancellationInterruptsBlockedSqlAndReleasesItsApplicationLock()
    {
        await using var holder = await HoldTableLockAsync();
        await using var transaction = holder.Transaction;
        await using var context = Database.CreateContext();
        using var cancel = new CancellationTokenSource(TimeSpan.FromMilliseconds(150));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test, cancel.Token));
        await transaction.RollbackAsync();
        Assert.Empty(await context.GusCallPermits.ToArrayAsync());
        Assert.True((await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test)).IsGranted);
    }

    [ManualGusQuotaSqlFact]
    public async Task CommandTimeoutFailsClosedAndDoesNotLeaveALockOrPermit()
    {
        await using var holder = await HoldTableLockAsync();
        await using var transaction = holder.Transaction;
        await using var context = Database.CreateContext();
        var error = await Assert.ThrowsAsync<GusRateLimiterUnavailableException>(() => Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test));
        Assert.Equal(-2, Assert.IsType<SqlException>(error.InnerException).Number);
        await transaction.RollbackAsync();
        Assert.Empty(await context.GusCallPermits.ToArrayAsync());
        Assert.True((await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test)).IsGranted);
    }

    [ManualGusQuotaSqlFact]
    public async Task MissingDatabaseIsUnavailableRatherThanQuotaExhaustion()
    {
        var target = new SqlConnectionStringBuilder(Database.InstanceConnectionString) { InitialCatalog = Database.Name + "_missing", ConnectTimeout = 1 };
        await using var context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(target.ConnectionString).Options);
        await Assert.ThrowsAsync<GusRateLimiterUnavailableException>(() => Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test));
    }

    [ManualGusQuotaSqlFact]
    public async Task ConcurrentAttemptsUseIndependentConnectionsAndRespectEverySlidingWindow()
        => Assert.True(await AttemptConcurrentlyAsync(0) > 0);

    [ManualGusQuotaSqlFact]
    public async Task ConcurrentAttemptsCannotExceedLastAvailableMinuteSlot()
    {
        await SeedAsync(119, 2);
        Assert.Equal(1, await AttemptConcurrentlyAsync(119));
    }

    [ManualGusQuotaSqlFact]
    public async Task ConcurrentAttemptsCannotExceedLastAvailableHourSlot()
    {
        await SeedAsync(5999, 61);
        Assert.Equal(1, await AttemptConcurrentlyAsync(5999));
    }

    [ManualGusQuotaSqlFact]
    public async Task BusinessTransactionRollbackCannotRefundACommittedPermit()
    {
        await using var context = Database.CreateContext();
        await using (var businessTransaction = await context.Database.BeginTransactionAsync())
        {
            Assert.True((await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test)).IsGranted);
            await businessTransaction.RollbackAsync();
        }
        Assert.Single(await context.GusCallPermits.ToArrayAsync());
    }

    private async Task<int> AttemptConcurrentlyAsync(int seededCount)
    {
        var start = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var attempts = Enumerable.Range(0, 48).Select(async _ =>
        {
            await using var context = Database.CreateContext();
            await start.Task;
            try { return (await Store(context).TryAcquireAsync(GusRateLimitEnvironment.Test)).IsGranted; }
            catch (GusRateLimiterUnavailableException error) when (error.InnerException is SqlException { Number: 51000 }) { return false; }
        }).ToArray();
        start.SetResult();
        var results = await Task.WhenAll(attempts);
        await using var verify = Database.CreateContext();
        var times = await verify.GusCallPermits.OrderBy(p => p.GrantedAtUtc).Select(p => p.GrantedAtUtc).ToArrayAsync();
        Assert.NotEmpty(times);
        var grantedCount = results.Count(granted => granted);
        Assert.Equal(seededCount + grantedCount, times.Length);
        foreach (var time in times.Skip(seededCount))
        {
            Assert.InRange(times.Count(t => t > time.AddSeconds(-1) && t <= time), 1, 3);
            Assert.InRange(times.Count(t => t > time.AddSeconds(-60) && t <= time), 1, 120);
            Assert.InRange(times.Count(t => t > time.AddSeconds(-3600) && t <= time), 1, 6000);
        }
        return grantedCount;
    }

    private static SqlGusQuotaStore Store(AppDbContext context) => new(context, TimeProvider.System);

    private async Task SeedAsync(int count, int ageSeconds)
    {
        await using var connection = new SqlConnection(Database.InstanceConnectionString);
        await connection.OpenAsync();
        using var command = connection.CreateCommand();
        command.CommandText = """
            DECLARE @now datetime2(7)=SYSUTCDATETIME();
            INSERT INTO dbo.GusCallPermits(Environment,GrantedAtUtc)
            SELECT TOP (@count) 0,DATEADD(second,-@age,@now) FROM sys.all_objects a CROSS JOIN sys.all_objects b;
            """;
        command.Parameters.AddWithValue("@count", count);
        command.Parameters.AddWithValue("@age", ageSeconds);
        Assert.Equal(count, await command.ExecuteNonQueryAsync());
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new SqlConnection(Database.InstanceConnectionString);
        await connection.OpenAsync();
        using var command = new SqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<TableLock> HoldTableLockAsync()
    {
        var connection = new SqlConnection(Database.InstanceConnectionString);
        await connection.OpenAsync();
        var transaction = (SqlTransaction)await connection.BeginTransactionAsync();
        try
        {
            using var command = new SqlCommand("SELECT COUNT(*) FROM dbo.GusCallPermits WITH(TABLOCKX,HOLDLOCK)", connection, transaction);
            await command.ExecuteScalarAsync();
            return new TableLock(connection, transaction);
        }
        catch { await transaction.DisposeAsync(); await connection.DisposeAsync(); throw; }
    }

    private sealed class TableLock(SqlConnection connection, SqlTransaction transaction) : IAsyncDisposable
    {
        public SqlTransaction Transaction { get; } = transaction;
        public ValueTask DisposeAsync() => connection.DisposeAsync();
    }
}

public sealed class ManualGusQuotaSqlFactAttribute : FactAttribute
{
    public ManualGusQuotaSqlFactAttribute()
    {
        if (Environment.GetEnvironmentVariable("XKSEF_RUN_GUS_QUOTA_SQL_TESTS") != "1")
            Skip = "Manual isolated LocalDB tests. Select GusQuota.Sql.runsettings explicitly.";
    }
}

public sealed class GusQuotaSqlFixture : IAsyncLifetime
{
    public SqlServerTestDatabase? Database { get; private set; }
    public async Task InitializeAsync()
    {
        if (Environment.GetEnvironmentVariable("XKSEF_RUN_GUS_QUOTA_SQL_TESTS") != "1") return;
        Database = await SqlServerTestDatabase.CreateEmptyAsync();
        try
        {
            await using var context = Database.CreateContext();
            await context.Database.MigrateAsync();
        }
        catch { await Database.DisposeAsync(); Database = null; throw; }
    }
    public Task DisposeAsync() => Database?.DisposeAsync() ?? Task.CompletedTask;
}
