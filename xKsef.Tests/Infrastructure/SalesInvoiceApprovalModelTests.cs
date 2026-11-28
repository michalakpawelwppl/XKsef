using Microsoft.EntityFrameworkCore.Migrations.Operations;
using xKsef.Infrastructure.Migrations;

namespace xKsef.Tests.Infrastructure;

public sealed class SalesInvoiceApprovalModelTests
{
    [Fact]
    public void ApprovalMigrationOnlyAddsHistoryWithTenantForeignKeyAndUniqueRevision()
    {
        var migration = new AddSalesInvoiceApprovals();
        Assert.All(migration.UpOperations, x => Assert.True(x is CreateTableOperation or CreateIndexOperation));
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("SalesInvoiceApprovals", table.Name);
        var foreignKey = Assert.Single(table.ForeignKeys);
        Assert.Equal("SalesInvoices", foreignKey.PrincipalTable);
        Assert.Equal(new[] { "TenantId", "SalesInvoiceId" }, foreignKey.Columns);
        Assert.Contains(table.CheckConstraints, x => x.Sql.Contains("ISJSON([SnapshotJson])", StringComparison.Ordinal));
        var index = Assert.Single(migration.UpOperations.OfType<CreateIndexOperation>());
        Assert.True(index.IsUnique); Assert.Equal(new[] { "TenantId", "SalesInvoiceId", "ApprovedRevision" }, index.Columns);
        Assert.Equal(table.Name, Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations)).Name);
    }
}
