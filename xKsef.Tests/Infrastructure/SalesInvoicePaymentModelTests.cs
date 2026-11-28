using System.Numerics;
using Microsoft.EntityFrameworkCore;
using xKsef.Domain.SalesInvoices;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Infrastructure;

public sealed class SalesInvoicePaymentModelTests
{
    [Fact]
    public void ExistingJsonColumnStoresAllPaymentDataWithoutAMigrationAndReadsHistoricalDetails()
    {
        using var db = new AppDbContextFactory().CreateDbContext([]); Assert.False(db.Database.HasPendingModelChanges());
        var converter = db.Model.FindEntityType(typeof(SalesInvoiceDetails))!.FindProperty(nameof(SalesInvoiceDetails.Data))!.GetValueConverter()!;
        var data = new SalesInvoiceDetailsData { PaymentStatus = InvoicePaymentStatus.PartiallyPaid, PartialPayments = [new(10m, new(2026, 11, 28), 6)], CashDiscount = new("7 dni", "3%"),
            PaymentTerms = [new(null, BigInteger.Parse("123456789012345678901234567890"), "dni", "zdarzenie")], PaymentLink = "https://pay.example/?IPKSeF=001ABC123DEF4", KsefPaymentId = "001ABC123DEF4" };
        var read = Assert.IsType<SalesInvoiceDetailsData>(converter.ConvertFromProvider(converter.ConvertToProvider(data)));
        Assert.Equal(data.PaymentStatus, read.PaymentStatus); Assert.Equal(data.PartialPayments, read.PartialPayments); Assert.Equal(data.CashDiscount, read.CashDiscount);
        Assert.Equal(data.PaymentTerms, read.PaymentTerms); Assert.Equal(data.PaymentLink, read.PaymentLink); Assert.Equal(data.KsefPaymentId, read.KsefPaymentId);
        var old = Assert.IsType<SalesInvoiceDetailsData>(converter.ConvertFromProvider("{\"Version\":1,\"PaymentTerms\":[{\"DueDate\":null,\"Quantity\":14,\"Unit\":\"dni\",\"StartingEvent\":\"zdarzenie\"}]}"));
        Assert.Null(old.PaymentStatus); Assert.Null(old.PaymentDate); Assert.Empty(old.PartialPayments); Assert.Null(old.CashDiscount); Assert.Null(old.PaymentLink); Assert.Null(old.KsefPaymentId);
        Assert.Equal(new BigInteger(14), old.PaymentTerms[0].Quantity); Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }
}
