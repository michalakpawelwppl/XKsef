using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using xKsef.Domain.Invoices;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Migrations;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Infrastructure;

public sealed class StoredInvoiceModelAndMigrationTests
{
    private static readonly string[] NewColumns = ["BuyerName", "BuyerNip", "InvoicingDate", "NetAmount", "VatAmount", "VatAmountPln"];

    [Theory]
    [InlineData(nameof(StoredInvoice.BuyerNip), 10)]
    [InlineData(nameof(StoredInvoice.BuyerName), 512)]
    public void BuyerPropertiesAreNullableUnicodeStringsWithSpecifiedLengths(string name, int length)
    {
        using var context = CreateContext();
        var property = GetInvoice(context).FindProperty(name)!;

        Assert.Equal(typeof(string), property.ClrType);
        Assert.True(property.IsNullable);
        Assert.Equal(length, property.GetMaxLength());
        Assert.Equal($"nvarchar({length})", property.GetColumnType());
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql));
        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
    }

    [Theory]
    [InlineData(nameof(StoredInvoice.NetAmount))]
    [InlineData(nameof(StoredInvoice.VatAmount))]
    [InlineData(nameof(StoredInvoice.VatAmountPln))]
    public void AmountPropertiesAreNullableDecimalsWithoutDefaults(string name)
    {
        using var context = CreateContext();
        var property = GetInvoice(context).FindProperty(name)!;

        Assert.Equal(typeof(decimal?), property.ClrType);
        Assert.True(property.IsNullable);
        Assert.Equal(18, property.GetPrecision());
        Assert.Equal(2, property.GetScale());
        Assert.Equal("decimal(18,2)", property.GetColumnType());
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql));
        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
    }

    [Fact]
    public void InvoicingDateIsNullableDateTimeOffsetWithoutDefaults()
    {
        using var context = CreateContext();
        var property = GetInvoice(context).FindProperty(nameof(StoredInvoice.InvoicingDate))!;

        Assert.Equal(typeof(DateTimeOffset?), property.ClrType);
        Assert.True(property.IsNullable);
        Assert.Equal("datetimeoffset", property.GetColumnType());
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValueSql));
        Assert.Equal(ValueGenerated.Never, property.ValueGenerated);
    }

    [Fact]
    public void ExistingInvoiceKeyIndexTenantRelationshipAndXmlMappingArePreserved()
    {
        using var context = CreateContext();
        var invoice = GetInvoice(context);

        Assert.Equal("invoices", invoice.GetTableName());
        Assert.Equal(nameof(StoredInvoice.Id), Assert.Single(invoice.FindPrimaryKey()!.Properties).Name);
        var index = Assert.Single(invoice.GetIndexes());
        Assert.True(index.IsUnique);
        Assert.Equal(new[] { nameof(StoredInvoice.TenantId), nameof(StoredInvoice.KsefNumber) }, index.Properties.Select(p => p.Name));
        var foreignKey = Assert.Single(invoice.GetForeignKeys());
        Assert.Equal(typeof(Tenant), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(nameof(StoredInvoice.TenantId), Assert.Single(foreignKey.Properties).Name);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        Assert.True(foreignKey.IsRequired);
        var xml = invoice.FindProperty(nameof(StoredInvoice.XmlBytes))!;
        Assert.False(xml.IsNullable);
        Assert.Equal("varbinary(max)", xml.GetColumnType());
        Assert.Equal("_xmlBytes", xml.GetFieldName());
        Assert.Equal(PropertyAccessMode.Field, xml.GetPropertyAccessMode());
    }

    [Fact]
    public void InitialCreatesAllTablesAndNullableInvoiceFieldsWithoutHistoricalDefaults()
    {
        var migration = new InitialCreate();
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(13, tables.Length);
        var invoice = Assert.Single(tables, table => table.Name == "invoices");
        AssertAddedColumns(invoice.Columns.Where(column => NewColumns.Contains(column.Name)).Cast<MigrationOperation>().ToArray());
        Assert.All(invoice.Columns, column => Assert.Null(column.DefaultValue));
        Assert.All(migration.UpOperations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation));
        Assert.Empty(Assert.Single(tables, table => table.Name == "TaxEntities").CheckConstraints);
        var membership = Assert.Single(tables, table => table.Name == "UserTenants");
        Assert.True(Assert.Single(membership.Columns, column => column.Name == "PreferredKsefCredentialId").IsNullable);
        Assert.Equal(new[] { "UserId", "TenantId" }, membership.PrimaryKey!.Columns);
        Assert.Single(invoice.ForeignKeys, foreignKey => foreignKey.PrincipalTable == "Tenants" && foreignKey.OnDelete == ReferentialAction.Restrict);
        Assert.Contains(migration.UpOperations.OfType<CreateIndexOperation>(), index => index.Table == "invoices" && index.IsUnique && index.Columns.SequenceEqual(new[] { "TenantId", "KsefNumber" }));
    }

    [Fact]
    public void InitialDownDropsAllTablesBeforeTheirReferencedParents()
    {
        var migration = new InitialCreate();
        var dropped = migration.DownOperations.Select(operation => Assert.IsType<DropTableOperation>(operation).Name).ToArray();
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(tables.Select(table => table.Name).OrderBy(name => name), dropped.OrderBy(name => name));
        foreach (var table in tables)
            foreach (var foreignKey in table.ForeignKeys)
                Assert.True(Array.IndexOf(dropped, table.Name) < Array.IndexOf(dropped, foreignKey.PrincipalTable));
    }

    [Fact]
    public void MigrationOrderAndCurrentTablesIncludeTheContractorCatalog()
    {
        using var context = CreateContext();
        var migrations = context.GetService<IMigrationsAssembly>().Migrations.Keys.OrderBy(id => id).ToArray();
        Assert.Equal(7, migrations.Length);
        Assert.EndsWith("_InitialCreate", migrations[0]);
        Assert.EndsWith("_AddUniqueNormalizedEmail", migrations[1]);
        Assert.EndsWith("_AddBrowserSessions", migrations[2]);
        Assert.EndsWith("_AddContractorsCatalog", migrations[3]);
        Assert.EndsWith("_ExpandContractorsCatalog", migrations[4]);
        Assert.EndsWith("_AddGusCallPermits", migrations[5]);
        Assert.EndsWith("_AddSalesInvoiceDrafts", migrations[6]);
        var current = context.GetService<IDesignTimeModel>().Model;
        var operations = context.GetService<IMigrationsModelDiffer>().GetDifferences(null, current.GetRelationalModel());
        Assert.Equal(26, operations.OfType<CreateTableOperation>().Count());
        Assert.All(operations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation or EnsureSchemaOperation));
    }

    [Fact]
    public void MigrationTargetAndSnapshotMatchTheCurrentModel()
    {
        using var context = CreateContext();
        var current = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var target = InitializeModel(context, new AddSalesInvoiceDrafts().TargetModel);
        var snapshot = InitializeModel(context, new AppDbContextModelSnapshot().Model);
        var differ = context.GetService<IMigrationsModelDiffer>();

        Assert.Empty(differ.GetDifferences(target.GetRelationalModel(), current));
        Assert.Empty(differ.GetDifferences(snapshot.GetRelationalModel(), current));
        Assert.False(context.Database.HasPendingModelChanges());
    }

    [Fact]
    public void InitialSqlCreatesTheSchemaWithoutHistoricalDataUpdatesOrSeed()
    {
        using var context = CreateContext();
        var migration = new InitialCreate();
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, InitializeModel(context, migration.TargetModel));
        string sql = string.Join(Environment.NewLine, commands.Select(command => command.CommandText));
        Assert.Equal(13, System.Text.RegularExpressions.Regex.Matches(sql, @"CREATE TABLE \[").Count);
        Assert.DoesNotMatch(@"(?im)^\s*(UPDATE|INSERT|DELETE|TRUNCATE|ALTER|DROP|EXEC)\b", sql);
        Assert.DoesNotContain("991223a5-3173-4c96-a8af-b515065eec49", sql);
        Assert.DoesNotContain("Firma testowa", sql);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private static void AssertAddedColumns(IReadOnlyList<MigrationOperation> operations)
    {
        Assert.Equal(6, operations.Count);
        var columns = operations.Select(operation => Assert.IsType<AddColumnOperation>(operation)).ToArray();
        Assert.Equal(NewColumns, columns.Select(column => column.Name).OrderBy(name => name, StringComparer.Ordinal));
        foreach (var column in columns)
        {
            Assert.Equal("invoices", column.Table);
            Assert.True(column.IsNullable);
            Assert.Null(column.DefaultValue);
            Assert.Null(column.DefaultValueSql);
            Assert.Null(column.ComputedColumnSql);
            Assert.False(column.IsDestructiveChange);
            switch (column.Name)
            {
                case "BuyerName":
                case "BuyerNip":
                    int length = column.Name == "BuyerName" ? 512 : 10;
                    Assert.Equal(typeof(string), column.ClrType);
                    Assert.Equal(length, column.MaxLength);
                    Assert.Equal($"nvarchar({length})", column.ColumnType);
                    break;
                case "InvoicingDate":
                    Assert.Equal(typeof(DateTimeOffset), column.ClrType);
                    Assert.Equal("datetimeoffset", column.ColumnType);
                    break;
                default:
                    Assert.Equal(typeof(decimal), column.ClrType);
                    Assert.Equal("decimal(18,2)", column.ColumnType);
                    Assert.Equal(18, column.Precision);
                    Assert.Equal(2, column.Scale);
                    break;
            }
        }
    }


    private static IEntityType GetInvoice(AppDbContext context) => context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(StoredInvoice))!;

    private static IModel InitializeModel(AppDbContext context, IModel model) => context.GetService<IModelRuntimeInitializer>().Initialize(model, designTime: true);

    private static AppDbContext CreateContext()
    {
        // Model inspection and SQL generation only: no connection is opened and no API host is started.
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=offline.invalid;Database=OfflineModelOnly;Integrated Security=true;Connect Timeout=1")
            .Options;
        return new AppDbContext(options);
    }
}
