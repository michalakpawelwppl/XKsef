using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using xKsef.Domain.InvoiceNumberSeries;
using xKsef.Infrastructure.Migrations;
using xKsef.Infrastructure.Persistence;
using NumberSeries = xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries;

namespace xKsef.Tests.Infrastructure;

public sealed class InvoiceNumberSeriesModelTests
{
    [Fact]
    public void MigrationOnlyCreatesConfigurationHistoryAndDefaultsWithoutInvoiceOrCounterChanges()
    {
        var migration = new AddInvoiceNumberSeries();
        Assert.All(migration.UpOperations, operation => Assert.True(operation is CreateTableOperation or CreateIndexOperation));
        var tables = migration.UpOperations.OfType<CreateTableOperation>().ToArray();
        Assert.Equal(new[] { "InvoiceNumberSeries", "InvoiceNumberSeriesDefaults", "InvoiceNumberSeriesHistory" }, tables.Select(x => x.Name).Order().ToArray());
        foreach (var table in tables.Where(x => x.Name != "InvoiceNumberSeries"))
        {
            var foreignKey = Assert.Single(table.ForeignKeys);
            Assert.Equal(new[] { "TenantId", "SeriesId" }, foreignKey.Columns); Assert.Equal(new[] { "TenantId", "Id" }, foreignKey.PrincipalColumns);
        }
        Assert.Equal(tables.Select(x => x.Name).Order(), migration.DownOperations.Cast<DropTableOperation>().Select(x => x.Name).Order());
    }

    [Fact]
    public void ModelHasTenantScopedUniquenessAndConcurrencyAndMatchesMigrationOffline()
    {
        using var db = new AppDbContextFactory().CreateDbContext([]); var model = db.GetService<IDesignTimeModel>().Model;
        var series = model.FindEntityType(typeof(NumberSeries))!;
        Assert.True(series.FindProperty(nameof(NumberSeries.RowVersion))!.IsConcurrencyToken);
        Assert.True(series.FindProperty(nameof(NumberSeries.Revision))!.IsConcurrencyToken);
        Assert.Contains(series.GetIndexes(), x => x.IsUnique && x.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "NormalizedName" }));
        var defaults = model.FindEntityType(typeof(InvoiceNumberSeriesDefault))!;
        Assert.Equal(new[] { "TenantId", "InvoiceKind" }, defaults.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.True(defaults.FindProperty(nameof(InvoiceNumberSeriesDefault.RowVersion))!.IsConcurrencyToken);
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
