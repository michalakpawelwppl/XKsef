using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using xKsef.CredentialService.Data;
using xKsef.CredentialService.Migrations;

namespace xKsef.Tests.Infrastructure;

public sealed class KsefSessionModelAndMigrationTests
{
    [Fact]
    public void SessionHasItsOwnTableAndRequiredContextKey()
    {
        using var context = CreateContext();
        var session = Entity<KsefSession>(context);
        Assert.Equal("KsefSessions", session.GetTableName());
        Assert.Equal(nameof(KsefSession.Id), Assert.Single(session.FindPrimaryKey()!.Properties).Name);
        Assert.Equal(new[] { "AccessTokenValidUntil", "AuthTag", "ContextNip", "CreatedAt", "CredentialId", "EncryptedData", "EncryptedDek", "Id", "Nonce", "ProtectionType", "RefreshTokenValidUntil", "TenantId", "UserId" },
            session.GetProperties().Select(property => property.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(session.GetProperties(), property => Assert.False(property.IsNullable));
        Assert.Equal(typeof(Guid), session.FindProperty(nameof(KsefSession.TenantId))!.ClrType);
        Assert.Equal(typeof(Guid), session.FindProperty(nameof(KsefSession.CredentialId))!.ClrType);
        var index = Assert.Single(session.GetIndexes(), index => index.IsUnique);
        Assert.Equal(new[] { "UserId", "TenantId", "CredentialId", "ContextNip" }, index.Properties.Select(property => property.Name));
        Assert.Null(index.GetFilter());
    }

    [Theory]
    [InlineData(nameof(KsefSession.UserId), 450, "nvarchar(450)")]
    [InlineData(nameof(KsefSession.ContextNip), 10, "nvarchar(10)")]
    [InlineData(nameof(KsefSession.ProtectionType), 50, "nvarchar(50)")]
    [InlineData(nameof(KsefSession.Nonce), 12, "varbinary(12)")]
    [InlineData(nameof(KsefSession.AuthTag), 16, "varbinary(16)")]
    public void SessionPropertiesHaveBoundedStorage(string name, int length, string columnType)
    {
        using var context = CreateContext();
        var property = Entity<KsefSession>(context).FindProperty(name)!;
        Assert.Equal(length, property.GetMaxLength());
        Assert.Equal(columnType, property.GetColumnType());
        Assert.False(property.IsNullable);
    }

    [Theory]
    [InlineData(nameof(KsefSession.EncryptedData))]
    [InlineData(nameof(KsefSession.EncryptedDek))]
    public void EncryptedPayloadIsRequiredBinaryData(string name)
    {
        using var context = CreateContext();
        var property = Entity<KsefSession>(context).FindProperty(name)!;
        Assert.Equal(typeof(byte[]), property.ClrType);
        Assert.Equal("varbinary(max)", property.GetColumnType());
        Assert.False(property.IsNullable);
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql));
    }

    [Theory]
    [InlineData(nameof(KsefSession.AccessTokenValidUntil))]
    [InlineData(nameof(KsefSession.RefreshTokenValidUntil))]
    [InlineData(nameof(KsefSession.CreatedAt))]
    public void TokenExpirationsAndCreationKeepDateTimeOffsetWithoutDatabaseDefaults(string name)
    {
        using var context = CreateContext();
        var property = Entity<KsefSession>(context).FindProperty(name)!;
        Assert.Equal(typeof(DateTimeOffset), property.ClrType);
        Assert.Equal("datetimeoffset", property.GetColumnType());
        Assert.False(property.IsNullable);
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql));
    }

    [Fact]
    public void CompositeForeignKeyRequiresTheCredentialOwnerAndRestrictsDeletion()
    {
        using var context = CreateContext();
        var session = Entity<KsefSession>(context);
        var relationship = Assert.Single(session.GetForeignKeys());
        Assert.Equal(typeof(KsefCredential), relationship.PrincipalEntityType.ClrType);
        Assert.Equal(new[] { "CredentialId", "UserId" }, relationship.Properties.Select(property => property.Name));
        Assert.Equal(new[] { "Id", "UserId" }, relationship.PrincipalKey.Properties.Select(property => property.Name));
        Assert.True(relationship.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, relationship.DeleteBehavior);
        Assert.Contains(session.GetIndexes(), index => index.Properties.Select(property => property.Name).SequenceEqual(new[] { "CredentialId", "UserId" }));
    }

    [Fact]
    public void ChecksConstrainContextIdentifiersAndCiphertextShape()
    {
        using var context = CreateContext();
        var checks = Entity<KsefSession>(context).GetCheckConstraints().ToDictionary(check => check.Name!, check => check.Sql);
        Assert.Equal(7, checks.Count);
        Assert.Contains("[Id] <>", checks["CK_KsefSessions_Identifiers"]);
        Assert.Contains("[TenantId] <>", checks["CK_KsefSessions_Identifiers"]);
        Assert.Contains("[CredentialId] <>", checks["CK_KsefSessions_Identifiers"]);
        Assert.Contains("00000000-0000-0000-0000-000000000000", checks["CK_KsefSessions_Identifiers"]);
        Assert.Contains("LEN(LTRIM(RTRIM([UserId]))) > 0", checks["CK_KsefSessions_UserId"]);
        Assert.Contains("DATALENGTH([ContextNip]) = 20", checks["CK_KsefSessions_ContextNip"]);
        Assert.Contains("COLLATE Latin1_General_100_BIN2", checks["CK_KsefSessions_ContextNip"]);
        Assert.Contains("NOT LIKE N'%[^0-9]%'", checks["CK_KsefSessions_ContextNip"]);
        Assert.Contains("DATALENGTH([EncryptedData]) > 0", checks["CK_KsefSessions_EncryptedPayload"]);
        Assert.Contains("DATALENGTH([EncryptedDek]) > 0", checks["CK_KsefSessions_EncryptedPayload"]);
        Assert.Contains("DATALENGTH([Nonce]) = 12", checks["CK_KsefSessions_Nonce"]);
        Assert.Contains("DATALENGTH([AuthTag]) = 16", checks["CK_KsefSessions_AuthTag"]);
        Assert.Contains("LEN(LTRIM(RTRIM([ProtectionType]))) > 0", checks["CK_KsefSessions_ProtectionType"]);
    }

    [Fact]
    public void InitialCreatesOnlyCurrentSourceAndSessionColumns()
    {
        var migration = new InitialCreate();
        var source = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>(), table => table.Name == "KsefCredentials");
        Assert.Equal(13, source.Columns.Count);
        Assert.True(Assert.Single(source.Columns, column => column.Name == "TenantId").IsNullable);
        Assert.True(Assert.Single(source.Columns, column => column.Name == "ContextNip").IsNullable);
        Assert.False(Assert.Single(source.Columns, column => column.Name == "UserId").IsNullable);
        Assert.DoesNotContain(source.Columns, column => column.Name is "SourceCredentialId" or "ValidUntil");
        Assert.Equal(7, source.CheckConstraints.Count);
        Assert.Equal(new[] { "Id", "UserId" }, Assert.Single(source.UniqueConstraints).Columns);
    }

    [Fact]
    public void CredentialMetadataDefaultsRemainCompatibleWithExistingWriters()
    {
        using var context = CreateContext();
        var credential = Entity<KsefCredential>(context);
        var name = credential.FindProperty(nameof(KsefCredential.Name))!;
        Assert.Equal("nvarchar(200)", name.GetColumnType());
        Assert.Equal(200, name.GetMaxLength());
        Assert.False(name.IsNullable);
        Assert.Equal(string.Empty, name.GetDefaultValue());
        Assert.Equal(true, credential.FindProperty(nameof(KsefCredential.IsActive))!.GetDefaultValue());
        var instance = new KsefCredential();
        Assert.Equal(string.Empty, instance.Name);
        Assert.True(instance.IsActive);
    }

    [Fact]
    public void InitialUpCreatesSourcesSessionsAndTheirSecurityConstraints()
    {
        AssertInitialOperations(new InitialCreate().UpOperations);
    }

    [Fact]
    public void InitialIsTheOnlyMigrationAndMatchesCreationFromEmpty()
    {
        using var context = CreateContext();
        Assert.EndsWith("_InitialCreate", Assert.Single(context.GetService<IMigrationsAssembly>().Migrations).Key);
        var current = context.GetService<IDesignTimeModel>().Model;
        AssertInitialOperations(context.GetService<IMigrationsModelDiffer>().GetDifferences(null, current.GetRelationalModel()));
    }

    [Fact]
    public void InitialDownDropsSessionsBeforeTheirSourceCredentials()
    {
        var operations = new InitialCreate().DownOperations;
        Assert.Equal(new[] { "KsefSessions", "KsefCredentials" }, operations.Select(operation => Assert.IsType<DropTableOperation>(operation).Name));
    }

    [Fact]
    public void SnapshotAndMigrationTargetMatchRuntimeModel()
    {
        using var context = CreateContext();
        var differ = context.GetService<IMigrationsModelDiffer>();
        var current = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        foreach (IModel model in new[] { new InitialCreate().TargetModel, context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model })
            Assert.Empty(differ.GetDifferences(Initialize(context, model).GetRelationalModel(), current));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void SqlGenerationContainsConstraintsWithoutChangingExistingDataOrColumns()
    {
        using var context = CreateContext();
        var migration = new InitialCreate();
        string sql = string.Join(Environment.NewLine, context.GetService<IMigrationsSqlGenerator>()
            .Generate(migration.UpOperations, Initialize(context, migration.TargetModel)).Select(command => command.CommandText));
        Assert.Contains("CREATE TABLE [KsefSessions]", sql);
        Assert.Contains("FOREIGN KEY ([CredentialId], [UserId]) REFERENCES [KsefCredentials] ([Id], [UserId])", sql);
        Assert.Contains("CREATE UNIQUE INDEX [IX_KsefSessions_UserId_TenantId_CredentialId_ContextNip]", sql);
        Assert.Contains("CHECK (DATALENGTH([ContextNip]) = 20", sql);
        Assert.Contains("[AccessTokenValidUntil] datetimeoffset NOT NULL", sql);
        Assert.Contains("[RefreshTokenValidUntil] datetimeoffset NOT NULL", sql);
        Assert.DoesNotMatch(@"(?im)^\s*(DELETE|UPDATE|INSERT|TRUNCATE|DROP)\b", sql);
        Assert.DoesNotContain("ALTER COLUMN", sql, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public void DesignTimeFactoryUsesOfflineModelOnlyWithoutOpeningAConnection()
    {
        using var context = new CredentialsDbContextFactory().CreateDbContext([]);
        Assert.Contains("offline.invalid", context.Database.GetConnectionString());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
        Assert.Equal(2, context.GetService<IDesignTimeModel>().Model.GetEntityTypes().Count());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private static void AssertInitialOperations(IReadOnlyList<MigrationOperation> operations)
    {
        Assert.Equal(6, operations.Count);
        var tables = operations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "KsefCredentials", "KsefSessions" }, tables.Select(table => table.Name));
        Assert.All(tables, table => Assert.Equal(7, table.CheckConstraints.Count));
        var source = tables[0];
        Assert.Equal(true, Assert.Single(source.Columns, column => column.Name == "IsActive").DefaultValue);
        Assert.Equal(string.Empty, Assert.Single(source.Columns, column => column.Name == "Name").DefaultValue);
        Assert.Equal(new[] { "Id", "UserId" }, Assert.Single(source.UniqueConstraints).Columns);
        var session = tables[1];
        Assert.Equal(13, session.Columns.Count);
        Assert.All(session.Columns, column => Assert.False(column.IsNullable));
        var foreignKey = Assert.Single(session.ForeignKeys);
        Assert.Equal(new[] { "CredentialId", "UserId" }, foreignKey.Columns);
        Assert.Equal("KsefCredentials", foreignKey.PrincipalTable);
        Assert.Equal(new[] { "Id", "UserId" }, foreignKey.PrincipalColumns);
        Assert.Equal(ReferentialAction.Restrict, foreignKey.OnDelete);
        var indexes = operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Equal(4, indexes.Length);
        Assert.Equal(2, indexes.Count(index => index.Table == "KsefCredentials" && !index.IsUnique));
        Assert.Contains(indexes, index => index.Table == "KsefSessions" && index.IsUnique && index.Columns.SequenceEqual(new[] { "UserId", "TenantId", "CredentialId", "ContextNip" }));
        Assert.All(operations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation));
    }
    private static IEntityType Entity<T>(CredentialsDbContext context) => context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(T))!;
    private static IModel Initialize(CredentialsDbContext context, IModel model) => context.GetService<IModelRuntimeInitializer>().Initialize(model, designTime: true);
    private static CredentialsDbContext CreateContext() => new CredentialsDbContextFactory().CreateDbContext([]);
}
