using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Persistence;
using xKsef.Tests.Integration;

namespace xKsef.Tests.Infrastructure;

public sealed class SalesInvoiceSettlementModelTests
{
    [Fact]
    public async Task SettlementMigrationPreservesExistingApprovalJsonAndAuditWithoutBackfillingResults()
    {
        await using var database = await SqlServerTestDatabase.CreateEmptyAsync(); await using var db = database.CreateContext();
        await db.GetService<IMigrator>().MigrateAsync("20261128151346_SimplifySalesInvoiceApprovalSnapshots");
        var tenant = new Tenant { Name = "Rozliczenie", TaxEntity = new() { Id = Guid.NewGuid(), Name = "Firma", Nip = "8567346215" } };
        db.Tenants.Add(tenant); await db.SaveChangesAsync();
        var invoiceId = Guid.NewGuid(); var approvalId = Guid.NewGuid(); var now = DateTimeOffset.UtcNow; var json = "{\"historical\":true}";
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SalesInvoices (Id,TenantId,Status,Kind,Currency,VatMethod,GlobalDiscountBasis,PdfLanguage,CreatedAt,UpdatedAt,CreatedBy,UpdatedBy,Revision,CalculationVersion,CalculationComplete,GlobalDiscountAmount) VALUES ({invoiceId},{tenant.Id},1,0,'PLN',0,0,0,{now},{now},'actor','actor',2,'sales-v3',0,0)");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SalesInvoiceApprovals (Id,TenantId,SalesInvoiceId,ApprovedBy,ApprovedAt,SourceRevision,ApprovedRevision,SnapshotJson) VALUES ({approvalId},{tenant.Id},{invoiceId},'actor',{now},1,2,{json})");
        var before = await db.Set<SalesInvoiceApproval>().AsNoTracking().SingleAsync(x => x.Id == approvalId);
        await db.Database.MigrateAsync();
        var after = await db.Set<SalesInvoiceApproval>().AsNoTracking().SingleAsync(x => x.Id == approvalId);
        Assert.Equal(System.Text.Json.JsonSerializer.Serialize(before), System.Text.Json.JsonSerializer.Serialize(after)); Assert.Equal(json, after.SnapshotJson);
        Assert.Null(await db.SalesInvoices.Where(x => x.Id == invoiceId).Select(x => x.SettlementAmounts).SingleAsync());
    }
    [Fact]
    public void SettlementInputsAndPersistedResultsRoundTripAndHistoricalDetailsStayAbsent()
    {
        using var db = new AppDbContextFactory().CreateDbContext([]); Assert.False(db.Database.HasPendingModelChanges());
        var detailsConverter = db.Model.FindEntityType(typeof(SalesInvoiceDetails))!.FindProperty(nameof(SalesInvoiceDetails.Data))!.GetValueConverter()!;
        var data = new SalesInvoiceDetailsData { Settlement = new() { Charges = [new(17m, "Powód")], Deductions = [new(300m, "Saldo")] } };
        var restored = Assert.IsType<SalesInvoiceDetailsData>(detailsConverter.ConvertFromProvider(detailsConverter.ConvertToProvider(data)));
        Assert.Equal(data.Settlement.Charges, restored.Settlement!.Charges); Assert.Equal(data.Settlement.Deductions, restored.Settlement.Deductions);
        var historical = Assert.IsType<SalesInvoiceDetailsData>(detailsConverter.ConvertFromProvider("{\"Version\":1}")); Assert.Null(historical.Settlement);
        var resultProperty = db.Model.FindEntityType(typeof(SalesInvoice))!.FindProperty(nameof(SalesInvoice.SettlementAmounts))!;
        Assert.True(resultProperty.IsNullable); var converter = resultProperty.GetValueConverter()!;
        var result = new InvoiceSettlementAmounts(17m, 300m, null, 37m);
        Assert.Equal(result, converter.ConvertFromProvider(converter.ConvertToProvider(result)));
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
