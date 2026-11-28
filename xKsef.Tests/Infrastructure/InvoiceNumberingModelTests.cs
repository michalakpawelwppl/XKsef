using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using xKsef.Domain.InvoiceNumberSeries;
using xKsef.Infrastructure.Migrations;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Infrastructure;

public sealed class InvoiceNumberingModelTests
{
    [Fact]
    public void CountersAndHeldNumbersHaveTenantScopedKeysAndApprovalForeignKeys()
    {
        using var db = new AppDbContextFactory().CreateDbContext([]); var model = db.GetService<IDesignTimeModel>().Model;
        var counter = model.FindEntityType(typeof(InvoiceNumberCounter))!;
        Assert.Equal(new[] { "TenantId", "SeriesId", "Period" }, counter.FindPrimaryKey()!.Properties.Select(x => x.Name));
        Assert.DoesNotContain(counter.GetProperties(), x => x.Name.Contains("Kind", StringComparison.Ordinal));
        Assert.True(counter.FindProperty(nameof(InvoiceNumberCounter.RowVersion))!.IsConcurrencyToken);
        var reservation = model.FindEntityType(typeof(InvoiceNumberReservation))!;
        foreach (var columns in new[] { new[] { "TenantId", "InvoiceId" }, new[] { "TenantId", "NormalizedNumber" }, new[] { "TenantId", "SeriesId", "Period", "NumericValue" } })
            Assert.Contains(reservation.GetIndexes(), x => x.IsUnique && x.GetFilter() == "[HoldsNumber] = 1" && x.Properties.Select(p => p.Name).SequenceEqual(columns));
        Assert.Contains(reservation.GetForeignKeys(), x => x.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "InvoiceId", "ApprovalId", "ApprovedRevision" }));
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
    [Fact]
    public void MigrationCreatesCountersAndReservationsWithoutAnyIssuanceData()
    {
        var migration = new AddInvoiceNumberReservations();
        Assert.Equal(new[] { "InvoiceNumberCounters", "InvoiceNumberReservations" }, migration.UpOperations.OfType<CreateTableOperation>().Select(x => x.Name).Order());
        Assert.DoesNotContain(migration.UpOperations, x => x is InsertDataOperation or UpdateDataOperation or DeleteDataOperation or SqlOperation);
        Assert.Contains(migration.UpOperations.OfType<AddCheckConstraintOperation>(), x => x.Name == "CK_SalesInvoices_FinalNumber");
    }
}
