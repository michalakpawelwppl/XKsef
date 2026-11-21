using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using xKsef.Infrastructure.Migrations;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Infrastructure;

public sealed class ContractorMigrationTests
{
    [Fact]
    public void UpgradeResetsOnlyCatalogInDependencyOrderWithoutGeneratingHistoricalAcronyms()
    {
        var operations = new ExpandContractorsCatalog().UpOperations;
        var reset = Assert.IsType<SqlOperation>(operations[0]);
        Assert.Equal("DELETE FROM [AddressesData]; DELETE FROM [Addresses]; DELETE FROM [ContractorsData]; DELETE FROM [Contractors];", reset.Sql);
        Assert.False(reset.SuppressTransaction);
        Assert.Single(operations.OfType<SqlOperation>());
        Assert.DoesNotContain(operations, operation => operation is DropTableOperation or DropForeignKeyOperation or DropPrimaryKeyOperation);
        var rename = Assert.Single(operations.OfType<RenameColumnOperation>());
        Assert.Equal("ContractorsData", rename.Table);
        Assert.Equal("Name", rename.Name);
        Assert.Equal("FullName", rename.NewName);
        var columns = operations.OfType<AddColumnOperation>().ToArray();
        Assert.Equal(7, columns.Length);
        var shortName = Assert.Single(columns, column => column.Name == "ShortName");
        Assert.False(shortName.IsNullable);
        Assert.Equal(100, shortName.MaxLength);
        Assert.Null(shortName.DefaultValue);
        var type = Assert.Single(columns, column => column.Name == "AddressType");
        Assert.False(type.IsNullable);
        Assert.Null(type.DefaultValue);
        Assert.Equal(false, Assert.Single(columns, column => column.Name == "IsEuVatRegistered").DefaultValue);
        var index = Assert.Single(operations.OfType<CreateIndexOperation>());
        Assert.True(index.IsUnique);
        Assert.Equal(new[] { "ContractorsDataId", "AddressType" }, index.Columns);
        Assert.Equal(4, operations.OfType<AddCheckConstraintOperation>().Count());
    }

    [Fact]
    public void DowngradeOnlyReversesCatalogSchemaAndRestoresTheSingleAddressIndex()
    {
        var operations = new ExpandContractorsCatalog().DownOperations;
        Assert.DoesNotContain(operations, operation => operation is SqlOperation or DropTableOperation or DropForeignKeyOperation);
        Assert.Equal(7, operations.OfType<DropColumnOperation>().Count());
        Assert.Equal(4, operations.OfType<DropCheckConstraintOperation>().Count());
        var index = Assert.Single(operations.OfType<CreateIndexOperation>());
        Assert.True(index.IsUnique);
        Assert.Equal(new[] { "ContractorsDataId" }, index.Columns);
        Assert.Equal("Name", Assert.Single(operations.OfType<RenameColumnOperation>()).NewName);
    }

    [Fact]
    public void DesignTimeFactoryDoesNotLoadRuntimeConfigurationOrOpenConnections()
    {
        using var context = new AppDbContextFactory().CreateDbContext([]);
        Assert.Contains("offline.invalid", context.Database.GetConnectionString());
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }
}
