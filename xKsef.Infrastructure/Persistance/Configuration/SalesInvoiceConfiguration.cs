using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.Tenants;

namespace xKsef.Infrastructure.Persistence.Configurations;

internal static class SalesInvoiceJson
{
    private static readonly JsonSerializerOptions Options = new() { NumberHandling = JsonNumberHandling.AllowReadingFromString | JsonNumberHandling.WriteAsString };
    public static string Write<T>(T value) => JsonSerializer.Serialize(value, Options);
    public static T Read<T>(string value) => JsonSerializer.Deserialize<T>(value, Options)!;
    public static void Map<T>(PropertyBuilder<T> property)
    {
        property.HasConversion(x => Write(x), x => Read<T>(x));
        property.Metadata.SetValueComparer(new ValueComparer<T>((a, b) => Write(a) == Write(b), x => Write(x).GetHashCode(), x => Read<T>(Write(x))));
    }
    public static void Money<TEntity>(EntityTypeBuilder<TEntity> builder) where TEntity : class
    {
        foreach (var property in builder.Metadata.GetProperties().Where(x => x.ClrType == typeof(decimal) || x.ClrType == typeof(decimal?)))
        { property.SetPrecision(18); property.SetScale(2); }
    }
}

internal sealed class SalesInvoiceConfiguration : IEntityTypeConfiguration<SalesInvoice>
{
    public void Configure(EntityTypeBuilder<SalesInvoice> b)
    {
        b.ToTable("SalesInvoices", t =>
        {
            t.HasCheckConstraint("CK_SalesInvoices_Status", "[Status] BETWEEN 0 AND 5");
            t.HasCheckConstraint("CK_SalesInvoices_Kind", "[Kind] BETWEEN 0 AND 6");
            t.HasCheckConstraint("CK_SalesInvoices_VatMethod", "[VatMethod] BETWEEN 0 AND 2");
            t.HasCheckConstraint("CK_SalesInvoices_DiscountBasis", "[GlobalDiscountBasis] IN (0,1) AND ([VatMethod] <> 1 OR [GlobalDiscountBasis] = 0) AND ([VatMethod] <> 2 OR [GlobalDiscountBasis] = 1)");
            t.HasCheckConstraint("CK_SalesInvoices_PdfLanguage", "[PdfLanguage] BETWEEN 0 AND 2");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Currency).HasMaxLength(3).IsRequired();
        b.Property(x => x.PlannedIssueDate).HasColumnType("date"); b.Property(x => x.SaleDate).HasColumnType("date");
        b.Property(x => x.Notes).HasMaxLength(4000); b.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired(); b.Property(x => x.UpdatedBy).HasMaxLength(450).IsRequired();
        b.Property(x => x.CalculationVersion).HasMaxLength(32).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnType("datetimeoffset"); b.Property(x => x.UpdatedAt).HasColumnType("datetimeoffset");
        b.Property(x => x.RowVersion).IsRowVersion(); b.Property(x => x.Revision).IsConcurrencyToken();
        SalesInvoiceJson.Map(b.Property(x => x.GlobalDiscount)); SalesInvoiceJson.Money(b);
        b.HasIndex(x => new { x.TenantId, x.Status, x.PlannedIssueDate, x.Id });
        b.HasMany(x => x.Lines).WithOne().HasForeignKey(x => new { x.TenantId, x.SalesInvoiceId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Parties).WithOne().HasForeignKey(x => new { x.TenantId, x.SalesInvoiceId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.TaxSummaries).WithOne().HasForeignKey(x => new { x.TenantId, x.SalesInvoiceId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.ExchangeRates).WithOne().HasForeignKey(x => new { x.TenantId, x.SalesInvoiceId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.References).WithOne().HasForeignKey(x => new { x.TenantId, x.SalesInvoiceId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasOne(x => x.Details).WithOne().HasForeignKey<SalesInvoiceDetails>(x => new { x.TenantId, x.SalesInvoiceId }).HasPrincipalKey<SalesInvoice>(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        foreach (var navigation in new[] { nameof(SalesInvoice.Lines), nameof(SalesInvoice.Parties), nameof(SalesInvoice.TaxSummaries), nameof(SalesInvoice.ExchangeRates), nameof(SalesInvoice.References) })
            b.Navigation(navigation).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class SalesInvoiceLineConfiguration : IEntityTypeConfiguration<SalesInvoiceLine>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceLine> b)
    {
        b.ToTable("SalesInvoiceLines", t =>
        {
            t.HasCheckConstraint("CK_SalesInvoiceLines_Ordinal", "[Ordinal] > 0");
            t.HasCheckConstraint("CK_SalesInvoiceLines_PriceBasis", "[PriceBasis] IN (0,1)");
            t.HasCheckConstraint("CK_SalesInvoiceLines_Inputs", "([Quantity] IS NULL OR [Quantity] >= 0) AND ([UnitPrice] IS NULL OR [UnitPrice] >= 0)");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasAlternateKey(x => new { x.TenantId, x.SalesInvoiceId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.SalesInvoiceId, x.Ordinal }).IsUnique();
        b.Property(x => x.Description).HasMaxLength(512); b.Property(x => x.Unit).HasMaxLength(256);
        b.Property(x => x.TaxCode).HasMaxLength(16).IsRequired();
        b.Property(x => x.ProductCode).HasMaxLength(50); b.Property(x => x.Gtin).HasMaxLength(20); b.Property(x => x.Pkwiu).HasMaxLength(50); b.Property(x => x.Cn).HasMaxLength(50); b.Property(x => x.Gtu).HasMaxLength(6);
        SalesInvoiceJson.Map(b.Property(x => x.Discount)); SalesInvoiceJson.Money(b);
        b.Property(x => x.Quantity).HasPrecision(22, 6); b.Property(x => x.UnitPrice).HasPrecision(22, 8);
    }
}

internal sealed class SalesInvoicePartyConfiguration : IEntityTypeConfiguration<SalesInvoicePartySnapshot>
{
    public void Configure(EntityTypeBuilder<SalesInvoicePartySnapshot> b)
    {
        b.ToTable("SalesInvoiceParties", t => t.HasCheckConstraint("CK_SalesInvoiceParties_Role", "[Role] IN (0,1)"));
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever(); b.HasIndex(x => new { x.TenantId, x.SalesInvoiceId, x.Role }).IsUnique();
        SalesInvoiceJson.Map(b.Property(x => x.Data));
    }
}

internal sealed class SalesInvoiceTaxSummaryConfiguration : IEntityTypeConfiguration<SalesInvoiceTaxSummary>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceTaxSummary> b)
    {
        b.ToTable("SalesInvoiceTaxSummaries"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.TaxCode).HasMaxLength(16).IsRequired();
        b.HasIndex(x => new { x.TenantId, x.SalesInvoiceId, x.TaxCode }).IsUnique(); SalesInvoiceJson.Money(b);
    }
}

internal sealed class SalesInvoiceExchangeRateConfiguration : IEntityTypeConfiguration<SalesInvoiceExchangeRateSnapshot>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceExchangeRateSnapshot> b)
    {
        b.ToTable("SalesInvoiceExchangeRates"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasIndex(x => new { x.TenantId, x.SalesInvoiceId, x.IsCurrent });
        b.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired(); b.Property(x => x.CreatedAt).HasColumnType("datetimeoffset");
        // Typed complex snapshot: rate precision is persisted as decimal columns, provenance alongside it.
        b.ComplexProperty(x => x.Data, data =>
        {
            data.Property(x => x.Rate).HasPrecision(28, 12); data.Property(x => x.OriginalRate).HasPrecision(28, 12); data.Property(x => x.QuotedUnits).HasPrecision(22, 6);
            data.Property(x => x.FromCurrency).HasMaxLength(3); data.Property(x => x.ToCurrency).HasMaxLength(3);
            data.Property(x => x.SourceReference).HasMaxLength(256); data.Property(x => x.SelectionRule).HasMaxLength(256); data.Property(x => x.OverrideReason).HasMaxLength(1000);
            data.Property(x => x.RateDate).HasColumnType("date"); data.Property(x => x.ReferenceDate).HasColumnType("date"); data.Property(x => x.RetrievedAt).HasColumnType("datetimeoffset");
        });
    }
}

internal sealed class SalesInvoiceDetailsConfiguration : IEntityTypeConfiguration<SalesInvoiceDetails>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceDetails> b)
    {
        b.ToTable("SalesInvoiceDetails"); b.HasKey(x => new { x.TenantId, x.SalesInvoiceId }); SalesInvoiceJson.Map(b.Property(x => x.Data));
    }
}

internal sealed class SalesInvoiceReferenceConfiguration : IEntityTypeConfiguration<SalesInvoiceReference>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceReference> b)
    {
        b.ToTable("SalesInvoiceReferences"); b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.Property(x => x.ExternalNumber).HasMaxLength(256); b.Property(x => x.KsefNumber).HasMaxLength(100); b.Property(x => x.IssueDate).HasColumnType("date");
        b.HasOne<SalesInvoice>().WithMany().HasForeignKey(x => new { x.TenantId, x.ReferencedSalesInvoiceId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesInvoiceLine>().WithMany().HasForeignKey(x => new { x.TenantId, x.ReferencedSalesInvoiceId, x.ReferencedLineId }).HasPrincipalKey(x => new { x.TenantId, x.SalesInvoiceId, x.Id }).OnDelete(DeleteBehavior.Restrict);
    }
}
