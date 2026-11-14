using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Models.Authorization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using xKsef.CredentialService.Data;
using xKsef.CredentialService.Security;
using xKsef.CredentialService.Services;

namespace xKsef.Tests.Infrastructure;

public sealed class KsefSessionStorageTests
{
    private const string Nip = "0123456789";
    private const string OtherNip = "9876543210";
    private const string User = "owner";

    [Fact]
    public async Task StoresOneAuthenticatedPackageAndRefreshPreservesOriginalTokenAndOffset()
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant);
        var accessUntil = DateTimeOffset.UtcNow.AddHours(1).ToOffset(TimeSpan.FromHours(2));
        var refreshUntil = DateTimeOffset.UtcNow.AddDays(3).ToOffset(TimeSpan.FromHours(-4));
        await service.SaveSessionTokensAsync(tenant, User, credential, Nip, "access", accessUntil, "refresh", refreshUntil);
        var original = await db.KsefSessions.AsNoTracking().SingleAsync();
        Assert.Single(await db.KsefCredentials.ToListAsync());
        Assert.False(original.EncryptedData.AsSpan().IndexOf(Encoding.UTF8.GetBytes("refresh")) >= 0);
        Assert.True(accessUntil.EqualsExact(original.AccessTokenValidUntil));
        Assert.True(refreshUntil.EqualsExact(original.RefreshTokenValidUntil));
        Assert.True(await service.HasValidSessionAsync(tenant, User, credential, Nip));

        var sdk = DispatchProxy.Create<IAuthorizationClient, AuthorizationProxy>();
        var proxy = (AuthorizationProxy)(object)sdk;
        proxy.Refresh = (token, _) =>
        {
            Assert.Equal("refresh", token);
            return Task.FromResult(new RefreshTokenResponse { AccessToken = new() { Token = "new-access", ValidUntil = accessUntil.AddHours(1) } });
        };
        Assert.True(await new KsefTokenRefreshService(service, sdk).TryRefreshAccessTokenAsync(tenant, User, credential, Nip));
        Assert.Equal(1, proxy.Calls);
        Assert.Equal("new-access", Text((await service.GetAccessTokenAsync(tenant, User, credential, Nip))!));
        var refresh = (await service.GetRefreshTokenAsync(tenant, User, credential, Nip))!;
        Assert.True(refreshUntil.EqualsExact(refresh.ValidUntil));
        Assert.Equal("refresh", Text(refresh));
        var updated = await db.KsefSessions.AsNoTracking().SingleAsync();
        Assert.Equal(original.Id, updated.Id);
        Assert.Equal(original.CreatedAt, updated.CreatedAt);
        Assert.False(original.Nonce.SequenceEqual(updated.Nonce));
        Assert.False(original.EncryptedDek.SequenceEqual(updated.EncryptedDek));

        byte[] plaintext = new ContextualSecretEncryptionService(store.Protector).Decrypt(Envelope(updated),
            SecretEncryptionContext.KsefSession(updated.Id, User, tenant, credential, Nip));
        try
        {
            Assert.True(plaintext.AsSpan().IndexOf(Encoding.UTF8.GetBytes("new-access")) >= 0);
            Assert.True(plaintext.AsSpan().IndexOf(Encoding.UTF8.GetBytes("refresh")) >= 0);
        }
        finally { CryptographicOperations.ZeroMemory(plaintext); }
        Assert.Throws<AuthenticationTagMismatchException>(() => new SecretEncryptionService(store.Protector).Decrypt(Envelope(updated), tenant, updated.Id));
    }

    [Theory]
    [InlineData("user")]
    [InlineData("tenant")]
    [InlineData("credential")]
    [InlineData("nip")]
    public async Task CompleteSessionContextIsRequiredForEveryOperation(string changed)
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant);
        await Pair(service, tenant, credential);
        string user = changed == "user" ? "another" : User;
        Guid requestedTenant = changed == "tenant" ? Guid.NewGuid() : tenant;
        Guid requestedCredential = changed == "credential" ? await Source(service, tenant) : credential;
        string nip = changed == "nip" ? OtherNip : Nip;
        Assert.Null(await service.GetAccessTokenAsync(requestedTenant, user, requestedCredential, nip));
        Assert.Null(await service.GetRefreshTokenAsync(requestedTenant, user, requestedCredential, nip));
        Assert.False(await service.HasValidSessionAsync(requestedTenant, user, requestedCredential, nip));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAccessTokenAsync(requestedTenant, user, requestedCredential, nip, "replacement", DateTimeOffset.UtcNow.AddHours(1)));
        Assert.Equal("refresh", Text((await service.GetRefreshTokenAsync(tenant, User, credential, Nip))!));
    }

    [Theory]
    [InlineData("Token")]
    [InlineData("Certificate")]
    public async Task InactiveSourceCannotReadUseWriteOrRefreshSession(string type)
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant, type);
        await Pair(service, tenant, credential);
        await db.KsefCredentials.Where(c => c.Id == credential).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
        Assert.Null(await service.GetAccessTokenAsync(tenant, User, credential, Nip));
        Assert.Null(await service.GetRefreshTokenAsync(tenant, User, credential, Nip));
        Assert.False(await service.HasValidSessionAsync(tenant, User, credential, Nip));
        Assert.Null(await service.GetAsync(credential, type == "Token" ? tenant : null, User, type, contextNip: type == "Token" ? Nip : null));
        Assert.Null(await service.GetCredentialTypeAsync(credential, tenant, User, "0123456789"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Pair(service, tenant, credential));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAccessTokenAsync(tenant, User, credential, Nip, "replacement", DateTimeOffset.UtcNow.AddHours(1)));
        Assert.False(await new KsefTokenRefreshService(service, null!).TryRefreshAccessTokenAsync(tenant, User, credential, Nip));
        Assert.Single(await db.KsefSessions.ToListAsync());
    }

    [Fact]
    public async Task CertificateSessionsForDifferentNipsRemainSeparate()
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant, "Certificate");
        await Pair(service, tenant, credential);
        await service.SaveSessionTokensAsync(tenant, User, credential, OtherNip, "other-access", DateTimeOffset.UtcNow.AddHours(1), "other-refresh", DateTimeOffset.UtcNow.AddDays(1));
        Assert.Equal(2, await db.KsefSessions.CountAsync());
        Assert.Equal("refresh", Text((await service.GetRefreshTokenAsync(tenant, User, credential, Nip))!));
        Assert.Equal("other-refresh", Text((await service.GetRefreshTokenAsync(tenant, User, credential, OtherNip))!));
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(1, false)]
    [InlineData(2, false)]
    [InlineData(10, true)]
    public async Task AccessValidityRetainsTwoMinuteMargin(int minutes, bool expected)
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant);
        await service.SaveSessionTokensAsync(tenant, User, credential, Nip, "access", DateTimeOffset.UtcNow.AddMinutes(minutes), "refresh", DateTimeOffset.UtcNow.AddDays(1));
        Assert.Equal(expected, await service.HasValidSessionAsync(tenant, User, credential, Nip));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task ExpiredRefreshCannotCallSdkOrChangeStoredPackage(int minutes)
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant);
        await service.SaveSessionTokensAsync(tenant, User, credential, Nip, "access", DateTimeOffset.UtcNow.AddMinutes(-1), "refresh", DateTimeOffset.UtcNow.AddMinutes(minutes));
        var original = await db.KsefSessions.AsNoTracking().SingleAsync();
        Assert.False(await new KsefTokenRefreshService(service, null!).TryRefreshAccessTokenAsync(tenant, User, credential, Nip));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAccessTokenAsync(tenant, User, credential, Nip, "replacement", DateTimeOffset.UtcNow.AddHours(1)));
        Assert.Equal(original.EncryptedData, (await db.KsefSessions.AsNoTracking().SingleAsync()).EncryptedData);
    }

    [Theory]
    [InlineData("session-id")]
    [InlineData("credential-id")]
    [InlineData("nip")]
    [InlineData("ciphertext")]
    [InlineData("expiration")]
    public async Task RejectsTamperedSessionPackageAndContext(string field)
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant, "Certificate");
        await Pair(service, tenant, credential);
        var session = await db.KsefSessions.SingleAsync();
        string requestedNip = Nip;
        if (field == "session-id")
            await db.Database.ExecuteSqlInterpolatedAsync($"UPDATE KsefSessions SET Id = {Guid.NewGuid()}");
        else if (field == "credential-id")
        {
            credential = await Source(service, tenant, "Certificate");
            session.CredentialId = credential;
        }
        else if (field == "nip") session.ContextNip = requestedNip = OtherNip;
        else if (field == "expiration") session.RefreshTokenValidUntil = session.RefreshTokenValidUntil.AddHours(1);
        else session.EncryptedData = session.EncryptedData.Select(b => (byte)(b ^ 1)).ToArray();
        if (field != "session-id") await db.SaveChangesAsync();
        await Assert.ThrowsAnyAsync<CryptographicException>(() => service.GetRefreshTokenAsync(tenant, User, credential, requestedNip));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TwoConcurrentUpdatesDetectConflictWithoutLosingWinningRefreshToken(bool firstIsReauthentication)
    {
        using var store = new KsefSessionTestStore();
        await using var setup = store.CreateContext();
        var service = store.CreateService(setup);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant);
        await Pair(service, tenant, credential);
        using var gate1 = new PausingEncryption(new ContextualSecretEncryptionService(store.Protector));
        using var gate2 = new PausingEncryption(new ContextualSecretEncryptionService(store.Protector));
        await using var db1 = store.CreateContext();
        await using var db2 = store.CreateContext();
        var first = store.CreateService(db1, gate1);
        var second = store.CreateService(db2, gate2);
        // Both operations read the same version before either may execute its UPDATE.
        Task task1 = Task.Run(() => firstIsReauthentication
            ? first.SaveSessionTokensAsync(tenant, User, credential, Nip, "winner-access", DateTimeOffset.UtcNow.AddHours(1), "winner-refresh", DateTimeOffset.UtcNow.AddDays(2))
            : first.SaveAccessTokenAsync(tenant, User, credential, Nip, "winner-access", DateTimeOffset.UtcNow.AddHours(1)));
        Task task2 = Task.Run(() => second.SaveAccessTokenAsync(tenant, User, credential, Nip, "stale-access", DateTimeOffset.UtcNow.AddHours(1)));
        try
        {
            await Task.WhenAll(gate1.Entered.Task, gate2.Entered.Task).WaitAsync(TimeSpan.FromSeconds(15));
            gate1.Release.Set();
            await task1.WaitAsync(TimeSpan.FromSeconds(15));
            gate2.Release.Set();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => task2);
        }
        finally
        {
            gate1.Release.Set();
            gate2.Release.Set();
            await Task.WhenAll(task1.ContinueWith(_ => { }), task2.ContinueWith(_ => { }));
        }
        Assert.Equal("winner-access", Text((await service.GetAccessTokenAsync(tenant, User, credential, Nip))!));
        Assert.Equal(firstIsReauthentication ? "winner-refresh" : "refresh", Text((await service.GetRefreshTokenAsync(tenant, User, credential, Nip))!));
        Assert.Single(await setup.KsefSessions.ToListAsync());
    }

    [Fact]
    public async Task ConcurrentCreationUsesUniqueContextKeyAndNeverOverwritesWinner()
    {
        using var store = new KsefSessionTestStore();
        await using var setup = store.CreateContext();
        var service = store.CreateService(setup);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant);
        using var gate1 = new PausingEncryption(new ContextualSecretEncryptionService(store.Protector));
        using var gate2 = new PausingEncryption(new ContextualSecretEncryptionService(store.Protector));
        await using var db1 = store.CreateContext();
        await using var db2 = store.CreateContext();
        var first = store.CreateService(db1, gate1);
        var second = store.CreateService(db2, gate2);
        Task a = Task.Run(() => Pair(first, tenant, credential));
        Task b = Task.Run(() => second.SaveSessionTokensAsync(tenant, User, credential, Nip, "stale", DateTimeOffset.UtcNow.AddHours(1), "stale-refresh", DateTimeOffset.UtcNow.AddDays(1)));
        try
        {
            await Task.WhenAll(gate1.Entered.Task, gate2.Entered.Task).WaitAsync(TimeSpan.FromSeconds(15));
            gate1.Release.Set();
            await a.WaitAsync(TimeSpan.FromSeconds(15));
            gate2.Release.Set();
            await Assert.ThrowsAsync<DbUpdateException>(() => b);
        }
        finally
        {
            gate1.Release.Set();
            gate2.Release.Set();
            await Task.WhenAll(a.ContinueWith(_ => { }), b.ContinueWith(_ => { }));
        }
        Assert.Equal("refresh", Text((await service.GetRefreshTokenAsync(tenant, User, credential, Nip))!));
        Assert.Single(await setup.KsefSessions.ToListAsync());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DatabaseFailureCannotPartiallyReplacePairOrRefresh(bool refreshOnly)
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant);
        await Pair(service, tenant, credential);
        var before = await db.KsefSessions.AsNoTracking().SingleAsync();
        store.Execute("CREATE TRIGGER RejectSessionUpdate BEFORE UPDATE ON KsefSessions BEGIN SELECT RAISE(ABORT, 'Test failure'); END;");
        await Assert.ThrowsAsync<SqliteException>(() => refreshOnly
            ? service.SaveAccessTokenAsync(tenant, User, credential, Nip, "replacement", DateTimeOffset.UtcNow.AddHours(1))
            : service.SaveSessionTokensAsync(tenant, User, credential, Nip, "replacement", DateTimeOffset.UtcNow.AddHours(1), "replacement-refresh", DateTimeOffset.UtcNow.AddDays(2)));
        var after = await db.KsefSessions.AsNoTracking().SingleAsync();
        Assert.Equal(before.EncryptedData, after.EncryptedData);
        Assert.Equal(before.EncryptedDek, after.EncryptedDek);
        Assert.Equal(before.AuthTag, after.AuthTag);
        Assert.True(before.RefreshTokenValidUntil.EqualsExact(after.RefreshTokenValidUntil));
        Assert.Equal("refresh", Text((await service.GetRefreshTokenAsync(tenant, User, credential, Nip))!));
    }

    [Fact]
    public async Task FailureDuringInsertLeavesNoSessionAndNoTrackedPendingWrite()
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant);
        store.Execute("CREATE TRIGGER RejectSessionInsert BEFORE INSERT ON KsefSessions BEGIN SELECT RAISE(ABORT, 'Test failure'); END;");
        await Assert.ThrowsAsync<DbUpdateException>(() => Pair(service, tenant, credential));
        Assert.Empty(await db.KsefSessions.ToListAsync());
        Assert.DoesNotContain(db.ChangeTracker.Entries(), entry => entry.Entity is KsefSession);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedSdkRefreshDoesNotChangeCorrectRefreshToken(bool cancel)
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant);
        await Pair(service, tenant, credential);
        var sdk = DispatchProxy.Create<IAuthorizationClient, AuthorizationProxy>();
        ((AuthorizationProxy)(object)sdk).Refresh = (_, _) => cancel
            ? Task.FromCanceled<RefreshTokenResponse>(new CancellationToken(true))
            : Task.FromException<RefreshTokenResponse>(new HttpRequestException("Test SDK failure."));
        var refresh = new KsefTokenRefreshService(service, sdk);
        if (cancel) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => refresh.TryRefreshAccessTokenAsync(tenant, User, credential, Nip));
        else await Assert.ThrowsAsync<HttpRequestException>(() => refresh.TryRefreshAccessTokenAsync(tenant, User, credential, Nip));
        Assert.Equal("access", Text((await service.GetAccessTokenAsync(tenant, User, credential, Nip))!));
        Assert.Equal("refresh", Text((await service.GetRefreshTokenAsync(tenant, User, credential, Nip))!));
    }

    [Fact]
    public async Task RevocationBeforeConditionalUpdateRejectsWriteAndPreservesPackage()
    {
        using var store = new KsefSessionTestStore();
        await using var setup = store.CreateContext();
        var service = store.CreateService(setup);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant);
        await Pair(service, tenant, credential);
        var before = await setup.KsefSessions.AsNoTracking().SingleAsync();
        using var gate = new PausingEncryption(new ContextualSecretEncryptionService(store.Protector));
        await using var updating = store.CreateContext();
        Task update = Task.Run(() => store.CreateService(updating, gate).SaveAccessTokenAsync(tenant, User, credential, Nip, "replacement", DateTimeOffset.UtcNow.AddHours(1)));
        try
        {
            await gate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(15));
            await setup.KsefCredentials.Where(c => c.Id == credential).ExecuteUpdateAsync(s => s.SetProperty(c => c.IsActive, false));
            gate.Release.Set();
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => update);
        }
        finally { gate.Release.Set(); await update.ContinueWith(_ => { }); }
        Assert.Equal(before.EncryptedData, (await setup.KsefSessions.AsNoTracking().SingleAsync()).EncryptedData);
    }

    [Fact]
    public async Task LegacySessionTypesCannotBeCreatedAndAreNotUsedAsFallback()
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant);
        foreach (string type in new[] { "AccessToken", "RefreshToken" })
            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveAsync(tenant, User, type, [1], contextNip: Nip));

        Assert.False(await service.HasValidSessionAsync(tenant, User, credential, Nip));
        Assert.Null(await service.GetAccessTokenAsync(tenant, User, credential, Nip));
        Assert.Null(await service.GetRefreshTokenAsync(tenant, User, credential, Nip));
        await Assert.ThrowsAsync<InvalidOperationException>(() => service.SaveAccessTokenAsync(tenant, User, credential, Nip, "new", DateTimeOffset.UtcNow.AddHours(1)));
        Assert.False(await new KsefTokenRefreshService(service, null!).TryRefreshAccessTokenAsync(tenant, User, credential, Nip));
        Assert.Single(await db.KsefCredentials.ToListAsync());
        Assert.Empty(await db.KsefSessions.ToListAsync());
    }

    [Fact]
    public async Task PlaintextPackageBuffersAreClearedOnReadAndFailedEncryption()
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var spy = new RecordingEncryption(new ContextualSecretEncryptionService(store.Protector));
        var service = store.CreateService(db, spy);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant);
        await Pair(service, tenant, credential);
        Assert.All(spy.LastEncryptedPlaintext!, value => Assert.Equal((byte)0, value));
        Assert.Equal("refresh", Text((await service.GetRefreshTokenAsync(tenant, User, credential, Nip))!));
        Assert.All(spy.LastDecryptedPlaintext!, value => Assert.Equal((byte)0, value));
        var before = await db.KsefSessions.AsNoTracking().SingleAsync();
        spy.FailEncryption = true;
        await Assert.ThrowsAsync<CryptographicException>(() => service.SaveAccessTokenAsync(tenant, User, credential, Nip, "replacement", DateTimeOffset.UtcNow.AddHours(1)));
        Assert.All(spy.LastEncryptedPlaintext!, value => Assert.Equal((byte)0, value));
        Assert.All(spy.LastDecryptedPlaintext!, value => Assert.Equal((byte)0, value));
        Assert.Equal(before.EncryptedData, (await db.KsefSessions.AsNoTracking().SingleAsync()).EncryptedData);
        Assert.Equal("refresh", Text((await service.GetRefreshTokenAsync(tenant, User, credential, Nip))!));
    }

    [Fact]
    public async Task CancellationBeforeUpdateCannotModifyExistingPackage()
    {
        using var store = new KsefSessionTestStore();
        await using var db = store.CreateContext();
        var service = store.CreateService(db);
        Guid tenant = Guid.NewGuid();
        Guid credential = await Source(service, tenant);
        await Pair(service, tenant, credential);
        var before = await db.KsefSessions.AsNoTracking().SingleAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.SaveAccessTokenAsync(tenant, User, credential, Nip, "replacement", DateTimeOffset.UtcNow.AddHours(1), new CancellationToken(true)));
        Assert.Equal(before.EncryptedData, (await db.KsefSessions.AsNoTracking().SingleAsync()).EncryptedData);
        Assert.Equal("refresh", Text((await service.GetRefreshTokenAsync(tenant, User, credential, Nip))!));
    }
    private static Task<Guid> Source(KsefCredentialService service, Guid tenant, string type = "Token") =>
        service.SaveAsync(type == "Token" ? tenant : null, User, type, [7], contextNip: type == "Token" ? Nip : null);

    private static Task Pair(KsefCredentialService service, Guid tenant, Guid credential) =>
        service.SaveSessionTokensAsync(tenant, User, credential, Nip, "access", DateTimeOffset.UtcNow.AddHours(1), "refresh", DateTimeOffset.UtcNow.AddDays(1));

    private static string Text(StoredSessionToken token)
    {
        try { return Encoding.UTF8.GetString(token.TokenBytes); }
        finally { CryptographicOperations.ZeroMemory(token.TokenBytes); }
    }

    private static EncryptedSecret Envelope(KsefSession session) => new()
    {
        EncryptedData = session.EncryptedData, EncryptedDek = session.EncryptedDek, Nonce = session.Nonce,
        AuthTag = session.AuthTag, ProtectionType = session.ProtectionType
    };

    public class AuthorizationProxy : DispatchProxy
    {
        public int Calls { get; private set; }
        public Func<string, CancellationToken, Task<RefreshTokenResponse>> Refresh { get; set; } = null!;
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        {
            Assert.Equal(nameof(IAuthorizationClient.RefreshAccessTokenAsync), targetMethod!.Name);
            Calls++;
            return Refresh((string)args![0]!, (CancellationToken)args[1]!);
        }
    }

    private sealed class RecordingEncryption(IContextualSecretEncryptionService inner) : IContextualSecretEncryptionService
    {
        public byte[]? LastEncryptedPlaintext { get; private set; }
        public byte[]? LastDecryptedPlaintext { get; private set; }
        public bool FailEncryption { get; set; }
        public EncryptedSecret Encrypt(byte[] data, SecretEncryptionContext context)
        {
            LastEncryptedPlaintext = data;
            if (FailEncryption) throw new CryptographicException("Test encryption failure.");
            return inner.Encrypt(data, context);
        }
        public byte[] Decrypt(EncryptedSecret secret, SecretEncryptionContext context)
        {
            LastDecryptedPlaintext = inner.Decrypt(secret, context);
            return LastDecryptedPlaintext;
        }
    }
    private sealed class PausingEncryption(IContextualSecretEncryptionService inner) : IContextualSecretEncryptionService, IDisposable
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ManualResetEventSlim Release { get; } = new(false);
        public EncryptedSecret Encrypt(byte[] data, SecretEncryptionContext context)
        {
            Entered.TrySetResult();
            if (!Release.Wait(TimeSpan.FromSeconds(20))) throw new TimeoutException("Test synchronization timed out.");
            return inner.Encrypt(data, context);
        }
        public byte[] Decrypt(EncryptedSecret secret, SecretEncryptionContext context) => inner.Decrypt(secret, context);
        public void Dispose() => Release.Dispose();
    }
}
