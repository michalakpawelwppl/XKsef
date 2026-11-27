using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using xKsef.Domain.SalesInvoices;
using xKsef.Infrastructure.Migrations;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Infrastructure;

public sealed class SalesInvoiceModelTests
{
    [Fact]
    public void MigrationAddsOnlySevenSalesTablesAndIndexesWithoutTouchingImportedInvoices()
    {
        var migration = new AddSalesInvoiceDrafts();
        Assert.All(migration.UpOperations, x => Assert.True(x is CreateTableOperation or CreateIndexOperation));
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(7, tables.Length); Assert.All(tables, x => Assert.StartsWith("SalesInvoice", x.Name));
        Assert.All(migration.UpOperations.OfType<CreateIndexOperation>(), x => Assert.StartsWith("SalesInvoice", x.Table));
        Assert.Equal(tables.Select(x => x.Name).Order(), migration.DownOperations.Select(x => Assert.IsType<DropTableOperation>(x).Name).Order());
        Assert.Contains(migration.UpOperations.OfType<CreateIndexOperation>(), x => x.Table == "SalesInvoiceLines" && x.IsUnique && x.Columns.SequenceEqual(new[] { "TenantId", "SalesInvoiceId", "Ordinal" }));
        Assert.All(tables.Where(x => x.Name != "SalesInvoices"), table => Assert.Contains(table.ForeignKeys, fk => fk.PrincipalTable == "SalesInvoices" && fk.Columns.Contains("TenantId")));
    }

    [Fact]
    public void PrecisionConcurrencyAndGeneratedKeysMatchTheDomainContract()
    {
        using var context = new AppDbContextFactory().CreateDbContext([]);
        var model = context.GetService<IDesignTimeModel>().Model;
        var invoice = model.FindEntityType(typeof(SalesInvoice))!;
        Assert.Equal(ValueGenerated.OnAddOrUpdate, invoice.FindProperty(nameof(SalesInvoice.RowVersion))!.ValueGenerated);
        Assert.True(invoice.FindProperty(nameof(SalesInvoice.RowVersion))!.IsConcurrencyToken);
        foreach (var entity in model.GetEntityTypes().Where(x => x.ClrType.Namespace == typeof(SalesInvoice).Namespace))
        {
            var id = entity.FindProperty("Id");
            if (id is not null) Assert.Equal(ValueGenerated.Never, id.ValueGenerated);
        }
        var line = model.FindEntityType(typeof(SalesInvoiceLine))!;
        AssertPrecision(line.FindProperty(nameof(SalesInvoiceLine.UnitPrice))!, 22, 8);
        AssertPrecision(line.FindProperty(nameof(SalesInvoiceLine.Quantity))!, 22, 6);
        AssertPrecision(line.FindProperty(nameof(SalesInvoiceLine.VatAmountPln))!, 18, 2);
        var rate = model.FindEntityType(typeof(SalesInvoiceExchangeRateSnapshot))!.FindComplexProperty(nameof(SalesInvoiceExchangeRateSnapshot.Data))!.ComplexType;
        AssertPrecision(rate.FindProperty(nameof(ExchangeRateData.Rate))!, 28, 12);
        AssertPrecision(rate.FindProperty(nameof(ExchangeRateData.OriginalRate))!, 28, 12);
        Assert.False(context.Database.HasPendingModelChanges());
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private static void AssertPrecision(IProperty property, int precision, int scale)
    { Assert.Equal(precision, property.GetPrecision()); Assert.Equal(scale, property.GetScale()); }
}
