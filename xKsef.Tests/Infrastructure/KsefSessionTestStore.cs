using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using xKsef.CredentialService.Data;
using xKsef.CredentialService.Security;
using xKsef.CredentialService.Services;

namespace xKsef.Tests.Infrastructure;

/// <summary>Private SQLite memory database. Never reads application configuration or opens an existing database.</summary>
internal sealed class KsefSessionTestStore : IDisposable
{
    private readonly SqliteConnection _keeper;
    public string ConnectionString { get; }
    public IKeyProtector Protector { get; } = new CopyKeyProtector();

    public KsefSessionTestStore()
    {
        ConnectionString = $"Data Source=session-{Guid.NewGuid():N};Mode=Memory;Cache=Shared;Pooling=False;Foreign Keys=True;Default Timeout=1";
        _keeper = new SqliteConnection(ConnectionString);
        _keeper.Open();
        // SQL Server-specific CHECK expressions cannot run in SQLite. Use a test-only schema,
        // retaining the session context unique key and owner FK. Production model tests cover SQL Server mapping.
        using var command = _keeper.CreateCommand();
        command.CommandText = """
            CREATE TABLE KsefCredentials (
                Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, TenantId TEXT NULL,
                Name TEXT NOT NULL DEFAULT '', IsActive INTEGER NOT NULL DEFAULT 1, CredentialType TEXT NOT NULL,
                ContextNip TEXT NULL, CreatedAt TEXT NOT NULL,
                EncryptedData BLOB NOT NULL, EncryptedDek BLOB NOT NULL, Nonce BLOB NOT NULL, AuthTag BLOB NOT NULL,
                ProtectionType TEXT NOT NULL, UNIQUE(Id, UserId));
            CREATE TABLE KsefSessions (
                Id TEXT NOT NULL PRIMARY KEY, UserId TEXT NOT NULL, TenantId TEXT NULL, CredentialId TEXT NOT NULL,
                ContextNip TEXT NOT NULL, EncryptedData BLOB NOT NULL, EncryptedDek BLOB NOT NULL,
                Nonce BLOB NOT NULL, AuthTag BLOB NOT NULL, ProtectionType TEXT NOT NULL,
                AccessTokenValidUntil TEXT NOT NULL, RefreshTokenValidUntil TEXT NOT NULL, CreatedAt TEXT NOT NULL,
                UNIQUE(UserId, TenantId, CredentialId, ContextNip),
                FOREIGN KEY(CredentialId, UserId) REFERENCES KsefCredentials(Id, UserId) ON DELETE RESTRICT);
            """;
        command.ExecuteNonQuery();
    }

    public CredentialsDbContext CreateContext(params IInterceptor[] interceptors) =>
        new(new DbContextOptionsBuilder<CredentialsDbContext>().UseSqlite(ConnectionString).ReplaceService<IModelCustomizer, SqliteTestModelCustomizer>().AddInterceptors(interceptors).Options);

    public KsefCredentialService CreateService(CredentialsDbContext context, IContextualSecretEncryptionService? encryption = null) =>
        new(context, encryption ?? new ContextualSecretEncryptionService(Protector));

    public void Execute(string sql)
    {
        using var command = _keeper.CreateCommand();
        command.CommandText = sql;
        command.ExecuteNonQuery();
    }

    public void Dispose() => _keeper.Dispose();

    public sealed class SqliteTestModelCustomizer(ModelCustomizerDependencies dependencies) : ModelCustomizer(dependencies)
    {
        public override void Customize(ModelBuilder modelBuilder, DbContext context)
        {
            base.Customize(modelBuilder, context);
            // SQLite cannot order DateTimeOffset. Only the source credential creation date needs ordering.
            // This converter is test-only; session dates retain their original offsets.
            modelBuilder.Entity<KsefCredential>().Property(x => x.CreatedAt)
                .HasConversion(value => value.UtcTicks, value => new DateTimeOffset(value, TimeSpan.Zero));
        }
    }
    private sealed class CopyKeyProtector : IKeyProtector
    {
        public byte[] Protect(byte[] key) => key.ToArray();
        public byte[] Unprotect(byte[] protectedKey) => protectedKey.ToArray();
    }
}
