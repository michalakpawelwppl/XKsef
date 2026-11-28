using Microsoft.EntityFrameworkCore;
using xKsef.Domain.SalesInvoices;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Infrastructure;

public sealed class SalesInvoiceBankAccountModelTests
{
    [Fact]
    public void ExistingJsonColumnStoresBothCollectionsAndReadsHistoricalDataWithoutSchemaChanges()
    {
        using var db = new AppDbContextFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges());
        var property = db.Model.FindEntityType(typeof(SalesInvoiceDetails))!.FindProperty(nameof(SalesInvoiceDetails.Data))!;
        var converter = property.GetValueConverter()!;
        var data = new SalesInvoiceDetailsData
        {
            BankAccounts = [new("seller/123", BankOwnAccountType: BankOwnAccountType.PurchasedReceivables)],
            FactorBankAccounts = [new("factor/123", "ABCDEFGH", "Bank", "Opis", BankOwnAccountType.CollectionsForSupplier)]
        };
        var json = Assert.IsType<string>(converter.ConvertToProvider(data));
        var restored = Assert.IsType<SalesInvoiceDetailsData>(converter.ConvertFromProvider(json));
        Assert.Equal(data.BankAccounts, restored.BankAccounts); Assert.Equal(data.FactorBankAccounts, restored.FactorBankAccounts);
        var historical = Assert.IsType<SalesInvoiceDetailsData>(converter.ConvertFromProvider("{\"Version\":1,\"BankAccounts\":[{\"Number\":\"1234567890\"}]}"));
        Assert.Empty(historical.FactorBankAccounts); Assert.Null(Assert.Single(historical.BankAccounts).BankOwnAccountType);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
