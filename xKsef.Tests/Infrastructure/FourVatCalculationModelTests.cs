using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Persistence;
using xKsef.Infrastructure.Persistence.Repositories;
using xKsef.Tests.Integration;

namespace xKsef.Tests.Infrastructure;

public sealed class FourVatCalculationModelTests
{
    [Fact]
    public void ModelAllowsExactlyFourMethodsAndKeepsAllocationAmountsAndExactCalculationDetails()
    {
        using var db = new AppDbContextFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges());
        var model = db.GetService<IDesignTimeModel>().Model;
        var root = model.FindEntityType(typeof(SalesInvoice))!;
        Assert.Contains(root.GetCheckConstraints(), x => x.Name == "CK_SalesInvoices_VatMethod" && x.Sql == "[VatMethod] BETWEEN 0 AND 3");
        Assert.Contains(root.GetCheckConstraints(), x => x.Name == "CK_SalesInvoices_DiscountBasis" && x.Sql == "[GlobalDiscountBasis] IN (0,1)");
        var line = model.FindEntityType(typeof(SalesInvoiceLine))!;
        Assert.Equal(18, line.FindProperty(nameof(SalesInvoiceLine.VatBaseRoundingAdjustment))!.GetPrecision());
        Assert.Equal(2, line.FindProperty(nameof(SalesInvoiceLine.VatBaseRoundingAdjustment))!.GetScale());
        var converter = model.FindEntityType(typeof(SalesInvoiceTaxSummary))!.FindProperty(nameof(SalesInvoiceTaxSummary.CalculationDetails))!.GetValueConverter()!;
        var details = new VatCalculationDetails(1, VatCalculationMethod.ByNetTotals, PriceBasis.Net, 0.1234567890123456789012345678m, 23m);
        var stored = converter.ConvertToProvider(details);
        Assert.Equal(details, converter.ConvertFromProvider(stored));
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

    [Fact]
    public async Task MigrationRemapsAllLegacyValuesAndInvalidatesOnlyDraftResultsUntilExplicitRecalculation()
    {
        await using var database = await SqlServerTestDatabase.CreateEmptyAsync();
        var now = new DateTimeOffset(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);
        var tenant = new Tenant { Name = "VAT migration", TaxEntity = new() { Id = Guid.NewGuid(), Name = "VAT", Nip = "8567346215" } };
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();
        await using (var db = database.CreateContext())
        {
            await db.GetService<IMigrator>().MigrateAsync("20261128084146_AddBasicSalesInvoiceFa3Data");
            db.Tenants.Add(tenant); await db.SaveChangesAsync();
            for (var old = 0; old < 5; old++)
            {
                var id = ids[old]; var line = Guid.NewGuid(); var summary = Guid.NewGuid(); var basis = old == 2 ? 1 : 0;
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SalesInvoices (Id,TenantId,Status,Kind,Currency,VatMethod,GlobalDiscountBasis,PdfLanguage,CreatedAt,UpdatedAt,CreatedBy,UpdatedBy,Revision,CalculationVersion,CalculationComplete,NetAmount,VatAmount,GrossAmount,VatAmountPln,GlobalDiscountAmount) VALUES ({id},{tenant.Id},0,0,'PLN',{old},{basis},0,{now},{now},'legacy','legacy',1,'sales-v1',1,100,23,123,23,0)");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SalesInvoiceLines (Id,TenantId,SalesInvoiceId,Ordinal,Description,Unit,Quantity,UnitPrice,PriceBasis,TaxCode,NetAmount,VatAmount,GrossAmount,VatAmountPln,GlobalDiscountAllocation,VatRoundingAdjustment) VALUES ({line},{tenant.Id},{id},1,'Towar','szt.',1,123,1,'23',100,23,123,23,0,0)");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SalesInvoiceTaxSummaries (Id,TenantId,SalesInvoiceId,TaxCode,NetAmount,VatAmount,GrossAmount,VatAmountPln) VALUES ({summary},{tenant.Id},{id},'23',100,23,123,23)");
                await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO SalesInvoiceDetails (TenantId,SalesInvoiceId,Data) VALUES ({tenant.Id},{id},{"{\"Version\":1,\"PaymentTerms\":[],\"BankAccounts\":[],\"AdditionalDescriptions\":[]}"})");
            }
            await db.GetService<IMigrator>().MigrateAsync();
        }
        await using (var db = database.CreateContext())
        {
            var expected = new[] { 0, 2, 3, 0, 1 };
            var repository = new SalesInvoiceRepository(db);
            for (var i = 0; i < ids.Length; i++)
            {
                var invoice = (await repository.GetAsync(tenant.Id, ids[i], true, default))!;
                Assert.Equal(expected[i], (int)invoice.VatMethod); Assert.Equal(2, invoice.Revision);
                Assert.False(invoice.CalculationComplete); Assert.Null(invoice.VatAmount); Assert.Empty(invoice.TaxSummaries);
                Assert.Equal("sales-v1", invoice.CalculationVersion);
                Assert.Null(invoice.Lines[0].VatAmountOrigin); Assert.Null(invoice.Lines[0].VatAmount); Assert.Equal(123m, invoice.Lines[0].UnitPrice);
                invoice.UpdateDraft(invoice.ToDraft(), "recalculator", now.AddMinutes(1), new());
                await repository.SaveAsync(invoice, invoice.RowVersion, default);
                Assert.Equal("sales-v3", invoice.CalculationVersion); Assert.True(invoice.CalculationComplete);
                Assert.Equal(100m, invoice.NetAmount); Assert.Equal(23m, invoice.VatAmount); Assert.Equal(123m, invoice.GrossAmount);
                Assert.Equal(PriceBasis.Gross, invoice.Lines[0].PriceBasis); Assert.Equal(expected[i], (int)invoice.VatMethod);
            }
        }
    }
}
