using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Migrations;
using xKsef.Infrastructure.Persistence;
using xKsef.Tests.Integration;

namespace xKsef.Tests.Infrastructure;

public sealed class SalesInvoiceFa3ModelTests
{
    [Fact]
    public void ModelAndMigrationPreserveTenantKeysPrecisionAndVersionedJson()
    {
        using var db = new AppDbContextFactory().CreateDbContext([]);
        Assert.False(db.Database.HasPendingModelChanges());
        var model = db.GetService<IDesignTimeModel>().Model;
        var participant = model.FindEntityType(typeof(SalesInvoiceParticipant))!;
        Assert.Contains(participant.GetForeignKeys(), fk => fk.PrincipalEntityType.ClrType == typeof(SalesInvoice) && fk.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "SalesInvoiceId" }));
        Assert.Contains(participant.GetIndexes(), index => index.IsUnique && index.Properties.Select(p => p.Name).SequenceEqual(new[] { "TenantId", "SalesInvoiceId", "Ordinal" }));
        Assert.Equal(9, participant.FindProperty(nameof(SalesInvoiceParticipant.SharePercent))!.GetPrecision());
        Assert.Equal(6, participant.FindProperty(nameof(SalesInvoiceParticipant.SharePercent))!.GetScale());
        var treatment = model.FindEntityType(typeof(InvoiceTaxTreatmentData))!;
        Assert.Equal(9, treatment.FindProperty(nameof(InvoiceTaxTreatmentData.ForeignVatRate))!.GetPrecision());
        Assert.Equal(6, treatment.FindProperty(nameof(InvoiceTaxTreatmentData.ForeignVatRate))!.GetScale());
        var root = model.FindEntityType(typeof(SalesInvoice))!;
        Assert.True(root.FindProperty(nameof(SalesInvoice.RowVersion))!.IsConcurrencyToken);
        Assert.Contains(root.GetCheckConstraints(), x => x.Sql.Contains("JSON_VALUE([Fa3]", StringComparison.Ordinal));
        var converter = root.FindProperty(nameof(SalesInvoice.Fa3))!.GetValueConverter()!;
        Assert.Throws<JsonException>(() => converter.ConvertFromProvider("{\"Version\":999}"));
        Assert.Throws<JsonException>(() => converter.ConvertFromProvider("{}"));
        Assert.Throws<JsonException>(() => converter.ConvertFromProvider("{\"Version\":1,\"Qualifications\":null}"));
        Assert.Equal(QualificationValue.Unknown, ((SalesInvoiceFa3Data)converter.ConvertFromProvider("{\"Version\":1}")!).Qualifications.SplitPayment.Value);
        var approval = model.FindEntityType(typeof(SalesInvoiceApproval))!;
        Assert.Contains(approval.GetCheckConstraints(), x => x.Sql == "ISJSON([SnapshotJson]) = 1");
        var migration = new AddBasicSalesInvoiceFa3Data();
        Assert.DoesNotContain(migration.UpOperations, operation => operation is SqlOperation or UpdateDataOperation or DeleteDataOperation);
        Assert.All(migration.UpOperations.OfType<AddColumnOperation>(), column => Assert.True(column.Table is "SalesInvoices" or "SalesInvoiceLines"));
        Assert.Equal("SalesInvoiceParticipants", Assert.Single(migration.UpOperations.OfType<CreateTableOperation>()).Name);
        Assert.Equal(System.Data.ConnectionState.Closed, db.Database.GetDbConnection().State);
    }

}
