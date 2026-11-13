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
    public void UpAddsExactlySixNullableColumnsWithoutDefaultsOrOtherOperations()
    {
        var migration = new AddInvoiceBuyerAmountsAndInvoicingDate();
        AssertAddedColumns(migration.UpOperations);
    }

    [Fact]
    public void DownDropsOnlyTheSixAddedColumns()
    {
        var migration = new AddInvoiceBuyerAmountsAndInvoicingDate();
        AssertDroppedColumns(migration.DownOperations);
    }

    [Fact]
    public void FullModelDifferenceFromPreviousMigrationContainsOnlyTheSixNewColumns()
    {
        using var context = CreateContext();
        var previous = InitializeModel(context, new AddPreferredKsefCredentialId().TargetModel);
        var current = context.GetService<IDesignTimeModel>().Model;
        var differ = context.GetService<IMigrationsModelDiffer>();

        AssertAddedColumns(differ.GetDifferences(previous.GetRelationalModel(), current.GetRelationalModel()));
        AssertDroppedColumns(differ.GetDifferences(current.GetRelationalModel(), previous.GetRelationalModel()));
    }

    [Fact]
    public void MigrationTargetAndSnapshotMatchTheCurrentModel()
    {
        using var context = CreateContext();
        var current = context.GetService<IDesignTimeModel>().Model.GetRelationalModel();
        var target = InitializeModel(context, new AddInvoiceBuyerAmountsAndInvoicingDate().TargetModel);
        var snapshot = InitializeModel(context, new AppDbContextModelSnapshot().Model);
        var differ = context.GetService<IMigrationsModelDiffer>();

        Assert.Empty(differ.GetDifferences(target.GetRelationalModel(), current));
        Assert.Empty(differ.GetDifferences(snapshot.GetRelationalModel(), current));
    }

    [Fact]
    public void GeneratedSqlOnlyAddsTheSixNullableColumns()
    {
        using var context = CreateContext();
        var migration = new AddInvoiceBuyerAmountsAndInvoicingDate();
        var commands = context.GetService<IMigrationsSqlGenerator>().Generate(migration.UpOperations, InitializeModel(context, migration.TargetModel));

        Assert.Equal(6, commands.Count);
        foreach (var command in commands)
        {
            Assert.StartsWith("ALTER TABLE [invoices] ADD [", command.CommandText);
            Assert.Contains(" NULL;", command.CommandText);
            Assert.DoesNotContain("DEFAULT", command.CommandText, StringComparison.OrdinalIgnoreCase);
        }
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

    private static void AssertDroppedColumns(IReadOnlyList<MigrationOperation> operations)
    {
        Assert.Equal(6, operations.Count);
        var columns = operations.Select(operation => Assert.IsType<DropColumnOperation>(operation)).ToArray();
        Assert.Equal(NewColumns, columns.Select(column => column.Name).OrderBy(name => name, StringComparer.Ordinal));
        Assert.All(columns, column => Assert.Equal("invoices", column.Table));
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
