using System.Data.Common;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Infrastructure.Authentication;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

[Collection(LocalDbIntegrationCollection.Name)]
public sealed class BrowserSessionStoreTests
{
    [Fact]
    public async Task NewLoginsAreUniqueAndOnlyCanonicalIdentityIsPersisted()
    {
        await using var f = await Fixture.CreateAsync();
        var ticket = f.Ticket();
        ticket.Properties.StoreTokens([new AuthenticationToken { Name = "access_token", Value = "forbidden-jwt" }]);
        ticket.Properties.Items["Password"] = "forbidden-password";
        ticket.Principal.Identities.First().AddClaims([new(ClaimTypes.Role, "Admin"), new("TenantId", Guid.NewGuid().ToString()), new("KsefToken", "forbidden-ksef")]);
        string first = await f.Store.StoreAsync(ticket), second = await f.Store.StoreAsync(ticket);
        Assert.NotEqual(first, second); Assert.Equal(43, first.Length);
        var restored = (await f.Store.RetrieveAsync(first))!;
        Assert.Equal(f.User, restored.Principal.FindFirstValue(ClaimTypes.NameIdentifier));
        Assert.Equal(3, restored.Principal.Claims.Count()); Assert.Empty(restored.Properties.GetTokens());
        Assert.DoesNotContain("Password", restored.Properties.Items.Keys); Assert.False(restored.Properties.IsPersistent);
        var row = await f.RowAsync();
        Assert.Equal(64, row.IdHash.Length); Assert.DoesNotContain(first, row.IdHash);
        Assert.Equal(f.Clock.GetUtcNow().AddMinutes(30), row.IdleExpiresAt);
        Assert.Equal(f.Clock.GetUtcNow().AddHours(8), row.AbsoluteExpiresAt);
        Assert.False(row.ProtectedTicket.AsSpan().IndexOf(System.Text.Encoding.UTF8.GetBytes("forbidden")) >= 0);
    }

    [Fact]
    public async Task IdleExpiryCannotBeRenewedAfterTheBoundary()
    {
        await using var f = await Fixture.CreateAsync(); string key = await f.Store.StoreAsync(f.Ticket());
        f.Clock.Advance(TimeSpan.FromMinutes(20)); await f.Store.RenewAsync(key, f.Ticket());
        Assert.NotNull(await f.Store.RetrieveAsync(key));
        var before = await f.RowAsync(); f.Clock.Advance(TimeSpan.FromMinutes(30));
        Assert.Null(await f.Store.RetrieveAsync(key)); await f.Store.RenewAsync(key, f.Ticket());
        Assert.Equal(before.Version, (await f.RowAsync()).Version);
    }

    [Fact]
    public async Task AbsoluteExpiryNeverMovesEvenWithRepeatedRenewals()
    {
        await using var f = await Fixture.CreateAsync(); string key = await f.Store.StoreAsync(f.Ticket());
        var absolute = (await f.RowAsync()).AbsoluteExpiresAt;
        for (int i = 0; i < 23; i++) { f.Clock.Advance(TimeSpan.FromMinutes(20)); await f.Store.RenewAsync(key, f.Ticket()); }
        f.Clock.Advance(TimeSpan.FromMinutes(15)); await f.Store.RenewAsync(key, f.Ticket());
        var row = await f.RowAsync(); Assert.Equal(absolute, row.AbsoluteExpiresAt); Assert.Equal(absolute, row.IdleExpiresAt);
        f.Clock.Advance(TimeSpan.FromMinutes(5)); Assert.Null(await f.Store.RetrieveAsync(key));
        await f.Store.RenewAsync(key, f.Ticket()); Assert.Equal(row.Version, (await f.RowAsync()).Version);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RevocationWinsAgainstAnAlreadyPreparedRenewal(bool all)
    {
        await using var f = await Fixture.CreateAsync(); string key = await f.Store.StoreAsync(f.Ticket());
        string other = await f.Store.StoreAsync(f.Ticket(f.Other));
        f.Clock.Advance(TimeSpan.FromMinutes(10)); f.Gate.Enable(1);
        Task renewal = f.Store.RenewAsync(key, f.Ticket());
        try
        {
            await f.Gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15));
            if (all) Assert.Equal(1, await f.Store.RevokeAllAsync(f.User)); else await f.Store.RemoveAsync(key);
        }
        finally { f.Gate.Release.TrySetResult(); }
        await renewal; Assert.Null(await f.Store.RetrieveAsync(key)); Assert.NotNull(await f.Store.RetrieveAsync(other));
        await f.Store.RenewAsync(key, f.Ticket()); Assert.Null(await f.Store.RetrieveAsync(key));
    }

    [Fact]
    public async Task ConcurrentRenewalsUseCompareAndSwapWithoutChangingAbsoluteExpiry()
    {
        await using var f = await Fixture.CreateAsync(); string key = await f.Store.StoreAsync(f.Ticket()); var absolute = (await f.RowAsync()).AbsoluteExpiresAt;
        f.Clock.Advance(TimeSpan.FromMinutes(10)); f.Gate.Enable(2);
        var first = f.Store.RenewAsync(key, f.Ticket()); var second = f.Store.RenewAsync(key, f.Ticket());
        try { await f.Gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15)); }
        finally { f.Gate.Release.TrySetResult(); }
        await Task.WhenAll(first, second);
        Assert.Equal(new[] { 0, 1 }, f.Gate.Updated.Order()); Assert.NotNull(await f.Store.RetrieveAsync(key));
        Assert.Equal(absolute, (await f.RowAsync()).AbsoluteExpiresAt);
    }

    [Fact]
    public async Task ExpiredAndRevokedCleanupIsBatchedAndDoesNotResurrectDuringRenewal()
    {
        await using var f = await Fixture.CreateAsync(); string key = await f.Store.StoreAsync(f.Ticket());
        await f.Store.StoreAsync(f.Ticket());
        // The production cleanup is global. Run it only when every session belongs to this fixture.
        using (var scope = f.Provider.CreateScope()) Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().BrowserSessions.AnyAsync(row => row.UserId != f.User));
        f.Gate.Enable(1); Task renewal = f.Store.RenewAsync(key, f.Ticket());
        try
        {
            await f.Gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(15)); f.Clock.Advance(TimeSpan.FromMinutes(31));
            Assert.Equal(1, await f.Store.DeleteExpiredAsync(1)); Assert.Equal(1, await f.Store.DeleteExpiredAsync(1)); Assert.Equal(0, await f.Store.DeleteExpiredAsync(1));
        }
        finally { f.Gate.Release.TrySetResult(); }
        await renewal; Assert.Null(await f.Store.RetrieveAsync(key));
        string active = await f.Store.StoreAsync(f.Ticket()); string revoked = await f.Store.StoreAsync(f.Ticket());
        await f.Store.RemoveAsync(revoked); Assert.Equal(1, await f.Store.DeleteExpiredAsync()); Assert.NotNull(await f.Store.RetrieveAsync(active));
    }

    [Fact]
    public async Task RevokeAllAndForeignRenewalAreOwnerScoped()
    {
        await using var f = await Fixture.CreateAsync(); string key = await f.Store.StoreAsync(f.Ticket()); string second = await f.Store.StoreAsync(f.Ticket());
        string other = await f.Store.StoreAsync(f.Ticket(f.Other)); var before = await f.RowAsync();
        await f.Store.RenewAsync(key, f.Ticket(f.Other)); Assert.Equal(before.Version, (await f.RowAsync()).Version);
        Assert.Equal(2, await f.Store.RevokeAllAsync(f.User)); Assert.Equal(0, await f.Store.RevokeAllAsync(f.User));
        Assert.Null(await f.Store.RetrieveAsync(key)); Assert.Null(await f.Store.RetrieveAsync(second)); Assert.NotNull(await f.Store.RetrieveAsync(other));
        Assert.NotNull(await f.Store.RetrieveAsync(await f.Store.StoreAsync(f.Ticket())));
    }

    [Fact]
    public async Task TicketSurvivesNewStoreAndDataProtectionProviderInstances()
    {
        string directory = Path.Combine(Path.GetTempPath(), "xksef-browser-dp-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var f = await Fixture.CreateAsync(DataProtectionProvider.Create(new DirectoryInfo(directory)));
            string key = await f.Store.StoreAsync(f.Ticket());
            using var second = f.NewProvider(DataProtectionProvider.Create(new DirectoryInfo(directory)));
            Assert.NotNull(await second.GetRequiredService<ITicketStore>().RetrieveAsync(key));
            await second.GetRequiredService<ITicketStore>().RemoveAsync(key); Assert.Null(await f.Store.RetrieveAsync(key));
        }
        finally { if (Path.GetFullPath(directory).StartsWith(Path.GetFullPath(Path.GetTempPath()), StringComparison.OrdinalIgnoreCase) && Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task SecurityStampChangesAndTamperedPayloadsFailClosed()
    {
        await using var f = await Fixture.CreateAsync(); string key = await f.Store.StoreAsync(f.Ticket());
        using (var scope = f.Provider.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(user => user.Id == f.User).ExecuteUpdateAsync(s => s.SetProperty(user => user.SecurityStamp, "new-stamp"));
        Assert.Null(await f.Store.RetrieveAsync(key)); var before = await f.RowAsync(); await f.Store.RenewAsync(key, f.Ticket()); Assert.Equal(before.Version, (await f.RowAsync()).Version);
        string newer = await f.Store.StoreAsync(f.Ticket());
        using (var scope = f.Provider.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().BrowserSessions.Where(row => row.UserId == f.User).ExecuteUpdateAsync(s => s.SetProperty(row => row.ProtectedTicket, new byte[] { 1, 2, 3 }));
        Assert.Null(await f.Store.RetrieveAsync(newer));
    }

    [Fact]
    public async Task DatabaseFailurePropagatesWithoutCachedOrStatelessIdentity()
    {
        await using var f = await Fixture.CreateAsync(); string key = await f.Store.StoreAsync(f.Ticket());
        f.Gate.Fail = true;
        try { await Assert.ThrowsAsync<IOException>(() => f.Store.RetrieveAsync(key)); await Assert.ThrowsAsync<IOException>(() => f.Store.RenewAsync(key, f.Ticket())); await Assert.ThrowsAsync<IOException>(() => f.Store.RemoveAsync(key)); }
        finally { f.Gate.Fail = false; }
        Assert.NotNull(await f.Store.RetrieveAsync(key));
    }

    [Fact]
    public async Task MissingProtectionKeysCannotAuthenticateAndDeletedUsersHaveNoSessions()
    {
        await using var f = await Fixture.CreateAsync(); string key = await f.Store.StoreAsync(f.Ticket());
        using (var otherKeys = f.NewProvider(new EphemeralDataProtectionProvider())) Assert.Null(await otherKeys.GetRequiredService<ITicketStore>().RetrieveAsync(key));
        Assert.NotNull(await f.Store.RetrieveAsync(key));
        using (var scope = f.Provider.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(user => user.Id == f.User).ExecuteDeleteAsync();
        Assert.Null(await f.Store.RetrieveAsync(key)); await f.Store.RenewAsync(key, f.Ticket()); Assert.Null(await f.Store.RetrieveAsync(key));
    }

    [Fact]
    public async Task DatabaseExpiryMetadataIsBoundToTheProtectedTicket()
    {
        await using var f = await Fixture.CreateAsync(); string key = await f.Store.StoreAsync(f.Ticket());
        var row = await f.RowAsync(); var extended = row.AbsoluteExpiresAt.AddHours(1);
        using (var scope = f.Provider.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().BrowserSessions.Where(session => session.UserId == f.User)
            .ExecuteUpdateAsync(setters => setters.SetProperty(session => session.AbsoluteExpiresAt, extended));
        Assert.Null(await f.Store.RetrieveAsync(key)); await f.Store.RenewAsync(key, f.Ticket()); Assert.Null(await f.Store.RetrieveAsync(key));
    }
    [Fact]
    public async Task MissingAccountOrSecurityStampCannotCreateASession()
    {
        await using var f = await Fixture.CreateAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Store.StoreAsync(f.Ticket("missing-account")));
        using (var scope = f.Provider.CreateScope()) await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(user => user.Id == f.User).ExecuteUpdateAsync(s => s.SetProperty(user => user.SecurityStamp, (string?)null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => f.Store.StoreAsync(f.Ticket()));
        using var verification = f.Provider.CreateScope(); Assert.False(await verification.ServiceProvider.GetRequiredService<AppDbContext>().BrowserSessions.AnyAsync(row => row.UserId == f.User));
    }
    private sealed class Fixture : IAsyncDisposable
    {
        public string User { get; } = Guid.NewGuid().ToString(); public string Other { get; } = Guid.NewGuid().ToString();
        public Clock Clock { get; } = new(); public RenewGate Gate { get; } = new();
        public ServiceProvider Provider { get; private set; } = null!;
        public SqlBrowserTicketStore Store => Provider.GetRequiredService<SqlBrowserTicketStore>();
        private string Connection { get; set; } = null!;
        private IDataProtectionProvider Protection { get; set; } = null!;
        public ServiceProvider NewProvider(IDataProtectionProvider protection)
        {
            var services = new ServiceCollection();
            services.AddDbContext<AppDbContext>(options => options.UseSqlServer(Connection).AddInterceptors(Gate));
            services.AddSingleton<TimeProvider>(Clock); services.AddSingleton(protection);
            services.AddBrowserSessionStore(new ConfigurationBuilder().Build());
            return services.BuildServiceProvider();
        }
        public static async Task<Fixture> CreateAsync(IDataProtectionProvider? protection = null)
        {
            var f = new Fixture { Protection = protection ?? new EphemeralDataProtectionProvider() };
            var configuration = new ConfigurationBuilder().SetBasePath(AppContext.BaseDirectory).AddJsonFile("appsettings.Test.json").UseIsolatedSqlServer().Build();
            f.Connection = configuration.GetConnectionString("DefaultConnection")!;
            await SqlServerTestDatabase.VerifyAsync(f.Connection);
            f.Provider = f.NewProvider(f.Protection);
            try
            {
                using var scope = f.Provider.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                foreach (string id in new[] { f.User, f.Other }) db.Users.Add(new ApplicationUser { Id = id, UserName = "browser-session-" + id, NormalizedUserName = ("browser-session-" + id).ToUpperInvariant(), SecurityStamp = Guid.NewGuid().ToString() });
                await db.SaveChangesAsync(); return f;
            }
            catch { await f.DisposeAsync(); throw; }
        }
        public AuthenticationTicket Ticket(string? owner = null) => new(new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, owner ?? User) }, BrowserSessionOptions.AuthenticationScheme)), new AuthenticationProperties(), BrowserSessionOptions.AuthenticationScheme);
        public async Task<BrowserSession> RowAsync()
        {
            using var scope = Provider.CreateScope(); return await scope.ServiceProvider.GetRequiredService<AppDbContext>().BrowserSessions.AsNoTracking().Where(row => row.UserId == User).OrderBy(row => row.IdHash).FirstAsync();
        }
        public async ValueTask DisposeAsync()
        {
            Gate.Fail = false; Gate.Release.TrySetResult();
            try { using var scope = Provider.CreateScope(); await scope.ServiceProvider.GetRequiredService<AppDbContext>().Users.Where(user => user.Id == User || user.Id == Other).ExecuteDeleteAsync(); }
            finally { await Provider.DisposeAsync(); }
        }
    }
    private sealed class Clock : TimeProvider
    {
        private long _ticks = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero).AddMilliseconds(123).UtcTicks;
        public override DateTimeOffset GetUtcNow() => new(Interlocked.Read(ref _ticks), TimeSpan.Zero);
        public void Advance(TimeSpan elapsed) => Interlocked.Add(ref _ticks, elapsed.Ticks);
    }
    private sealed class RenewGate : DbCommandInterceptor
    {
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public System.Collections.Concurrent.ConcurrentBag<int> Updated { get; } = [];
        public bool Fail { get; set; } private int _expected; private int _arrivals;
        public void Enable(int expected) => _expected = expected;
        private static bool IsRenew(DbCommand command) => command.CommandText.StartsWith("UPDATE", StringComparison.Ordinal) && command.CommandText.Contains("[ProtectedTicket]", StringComparison.Ordinal);
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        { if (Fail) throw new IOException("Store unavailable"); return ValueTask.FromResult(result); }
        public override async ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(DbCommand command, CommandEventData data, InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Fail) throw new IOException("Store unavailable");
            if (_expected > 0 && IsRenew(command)) { if (Interlocked.Increment(ref _arrivals) == _expected) Arrived.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken); }
            return result;
        }
        public override ValueTask<int> NonQueryExecutedAsync(DbCommand command, CommandExecutedEventData data, int result, CancellationToken cancellationToken = default)
        { if (_expected > 0 && IsRenew(command)) Updated.Add(result); return ValueTask.FromResult(result); }
    }
}