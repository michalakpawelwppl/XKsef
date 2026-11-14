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
    public void ExistingCredentialColumnsAndSessionIndexRemainUnchanged()
    {
        using var context = CreateContext();
        var oldCredential = Initialize(context, new ExtendKsefSessionUniqueIndex().TargetModel).FindEntityType(typeof(KsefCredential).FullName!)!;
        var current = Initialize(context, new AddKsefSessionsAndCredentialMetadata().TargetModel).FindEntityType(typeof(KsefCredential).FullName!)!;
        foreach (var previous in oldCredential.GetProperties())
        {
            var property = current.FindProperty(previous.Name)!;
            Assert.Equal(previous.ClrType, property.ClrType);
            Assert.Equal(previous.IsNullable, property.IsNullable);
            Assert.Equal(previous.GetColumnType(), property.GetColumnType());
            Assert.Equal(previous.GetMaxLength(), property.GetMaxLength());
        }
        Assert.Equal(new[] { "IsActive", "Name" }, current.GetProperties().Select(property => property.Name).Except(oldCredential.GetProperties().Select(property => property.Name)).OrderBy(name => name, StringComparer.Ordinal));
        Assert.Equal(typeof(Guid), current.FindProperty(nameof(KsefCredential.TenantId))!.ClrType);
        Assert.NotNull(current.FindProperty("SourceCredentialId"));
        Assert.NotNull(current.FindProperty("ValidUntil"));
        var oldIndex = Assert.Single(oldCredential.GetIndexes());
        var index = Assert.Single(current.GetIndexes());
        Assert.Equal(oldIndex.Properties.Select(property => property.Name), index.Properties.Select(property => property.Name));
        Assert.Equal(oldIndex.GetFilter(), index.GetFilter());
        Assert.Equal(oldIndex.IsUnique, index.IsUnique);
        Assert.Equal(oldCredential.FindPrimaryKey()!.Properties.Select(property => property.Name), current.FindPrimaryKey()!.Properties.Select(property => property.Name));
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
    public void MigrationUpOnlyAddsTheSessionTableCredentialMetadataAndOwnerKey()
    {
        AssertAdditiveOperations(new AddKsefSessionsAndCredentialMetadata().UpOperations);
    }

    [Fact]
    public void CompleteDifferenceFromPreviousModelIsAdditive()
    {
        using var context = CreateContext();
        var previous = Initialize(context, new ExtendKsefSessionUniqueIndex().TargetModel);
        var current = Initialize(context, new AddKsefSessionsAndCredentialMetadata().TargetModel);
        AssertAdditiveOperations(context.GetService<IMigrationsModelDiffer>().GetDifferences(previous.GetRelationalModel(), current.GetRelationalModel()));
    }

    [Fact]
    public void MigrationDownOnlyReversesItsOwnAdditions()
    {
        var operations = new AddKsefSessionsAndCredentialMetadata().DownOperations;
        Assert.Equal(4, operations.Count);
        Assert.Equal("KsefSessions", Assert.IsType<DropTableOperation>(operations[0]).Name);
        var key = Assert.IsType<DropUniqueConstraintOperation>(operations[1]);
        Assert.Equal("KsefCredentials", key.Table);
        Assert.Equal("AK_KsefCredentials_Id_UserId", key.Name);
        var columns = operations.Skip(2).Select(operation => Assert.IsType<DropColumnOperation>(operation)).ToArray();
        Assert.Equal(new[] { "IsActive", "Name" }, columns.Select(column => column.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(columns, column => Assert.Equal("KsefCredentials", column.Table));
    }

    [Fact]
    public void SnapshotAndMigrationTargetMatchRuntimeModel()
    {
        using var context = CreateContext();
        var differ = context.GetService<IMigrationsModelDiffer>();
        var current = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        foreach (IModel model in new[] { new RebuildKsefSourceCredentials().TargetModel, context.GetService<IMigrationsAssembly>().ModelSnapshot!.Model })
            Assert.Empty(differ.GetDifferences(Initialize(context, model).GetRelationalModel(), current));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void SqlGenerationContainsConstraintsWithoutChangingExistingDataOrColumns()
    {
        using var context = CreateContext();
        var migration = new AddKsefSessionsAndCredentialMetadata();
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

    private static void AssertAdditiveOperations(IReadOnlyList<MigrationOperation> operations)
    {
        Assert.Equal(6, operations.Count);
        var columns = operations.OfType<AddColumnOperation>().OrderBy(column => column.Name, StringComparer.Ordinal).ToArray();
        Assert.Equal(new[] { "IsActive", "Name" }, columns.Select(column => column.Name));
        Assert.All(columns, column => Assert.Equal("KsefCredentials", column.Table));
        Assert.Equal(true, columns[0].DefaultValue);
        Assert.Equal(string.Empty, columns[1].DefaultValue);
        var ownerKey = Assert.Single(operations.OfType<AddUniqueConstraintOperation>());
        Assert.Equal("KsefCredentials", ownerKey.Table);
        Assert.Equal(new[] { "Id", "UserId" }, ownerKey.Columns);
        var table = Assert.Single(operations.OfType<CreateTableOperation>());
        Assert.Equal("KsefSessions", table.Name);
        Assert.Equal(13, table.Columns.Count);
        Assert.All(table.Columns, column => Assert.False(column.IsNullable));
        Assert.Equal(7, table.CheckConstraints.Count);
        var foreignKey = Assert.Single(table.ForeignKeys);
        Assert.Equal(new[] { "CredentialId", "UserId" }, foreignKey.Columns);
        Assert.Equal("KsefCredentials", foreignKey.PrincipalTable);
        Assert.Equal(new[] { "Id", "UserId" }, foreignKey.PrincipalColumns);
        Assert.Equal(Microsoft.EntityFrameworkCore.Migrations.ReferentialAction.Restrict, foreignKey.OnDelete);
        var indexes = operations.OfType<CreateIndexOperation>().ToArray();
        Assert.Equal(2, indexes.Length);
        Assert.All(indexes, index => Assert.Equal("KsefSessions", index.Table));
        Assert.Contains(indexes, index => index.IsUnique && index.Columns.SequenceEqual(new[] { "UserId", "TenantId", "CredentialId", "ContextNip" }));
        Assert.All(operations, operation => Assert.True(operation is AddColumnOperation or AddUniqueConstraintOperation or CreateTableOperation or CreateIndexOperation));
    }

    private static IEntityType Entity<T>(CredentialsDbContext context) => context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(T))!;
    private static IModel Initialize(CredentialsDbContext context, IModel model) => context.GetService<IModelRuntimeInitializer>().Initialize(model, designTime: true);
    private static CredentialsDbContext CreateContext() => new CredentialsDbContextFactory().CreateDbContext([]);
}
