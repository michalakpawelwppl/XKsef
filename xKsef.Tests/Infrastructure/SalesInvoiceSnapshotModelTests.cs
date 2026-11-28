using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.Tenants;
using xKsef.Tests.Integration;

namespace xKsef.Tests.Infrastructure;

public sealed class SalesInvoiceSnapshotModelTests
{
    [Fact]
    public async Task SnapshotMigrationRejectsExistingApprovalDataWithoutDeletingOrConvertingIt()
    {
        await using var database = await SqlServerTestDatabase.CreateEmptyAsync();
        await using var db = database.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync("20261128094943_EnableSalesInvoiceApprovalSnapshotV2");
        var tenant = new Tenant { Name = "Migration", TaxEntity = new() { Id = Guid.NewGuid(), Name = "Firma", Nip = "8567346215" } };
        db.Tenants.Add(tenant); await db.SaveChangesAsync();
        var invoiceId = Guid.NewGuid(); var id = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var json = "{\"legacy\":true}";
        // Seed the historical SQL shape, without relying on columns added to the current EF model later.
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SalesInvoices (Id,TenantId,Status,Kind,Currency,VatMethod,GlobalDiscountBasis,PdfLanguage,CreatedAt,UpdatedAt,CreatedBy,UpdatedBy,Revision,CalculationVersion,CalculationComplete,GlobalDiscountAmount) VALUES ({invoiceId},{tenant.Id},0,0,'PLN',0,0,0,{now},{now},'actor','actor',1,'sales-v3',0,0)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SalesInvoiceApprovals (Id,TenantId,SalesInvoiceId,ApprovedBy,ApprovedAt,SourceRevision,ApprovedRevision,SnapshotVersion,SnapshotJson) VALUES ({id},{tenant.Id},{invoiceId},'actor',{now},1,2,2,{json})");
        var error = await Assert.ThrowsAsync<SqlException>(() => db.GetService<IMigrator>().MigrateAsync());
        Assert.Equal(50002, error.Number);
        Assert.Equal(json, await db.Set<SalesInvoiceApproval>().Where(x => x.Id == id).Select(x => x.SnapshotJson).SingleAsync());
        Assert.Contains("20261128151346_SimplifySalesInvoiceApprovalSnapshots", await db.Database.GetPendingMigrationsAsync());
        await db.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM SalesInvoiceApprovals WHERE Id = {id}");
        await db.GetService<IMigrator>().MigrateAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync()); Assert.False(db.Database.HasPendingModelChanges());
    }
}
