using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.InvoiceNumberSeries;
using xKsef.Domain.Tenants;
using NumberSeries = xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries;

namespace xKsef.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceNumberSeriesConfiguration : IEntityTypeConfiguration<NumberSeries>
{
    public void Configure(EntityTypeBuilder<NumberSeries> b)
    {
        b.ToTable("InvoiceNumberSeries", t =>
        {
            t.HasCheckConstraint("CK_InvoiceNumberSeries_Configuration", "[MinimumDigits] BETWEEN 1 AND 19 AND [ResetMode] IN (0,1) AND [StartingNumber] > 0 AND [Revision] > 0");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.TenantId, x.Id });
        b.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.NormalizedName).HasMaxLength(100).IsRequired().UseCollation("Latin1_General_100_BIN2");
        b.HasIndex(x => new { x.TenantId, x.NormalizedName }).IsUnique();
        b.Property(x => x.RowVersion).IsRowVersion(); b.Property(x => x.Revision).IsConcurrencyToken();
        b.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired(); b.Property(x => x.UpdatedBy).HasMaxLength(450).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnType("datetimeoffset"); b.Property(x => x.UpdatedAt).HasColumnType("datetimeoffset");
        b.ComplexProperty(x => x.Configuration, c =>
        {
            c.Property(x => x.Name).HasColumnName("Name").HasMaxLength(100).IsRequired();
            c.Property(x => x.Prefix).HasColumnName("Prefix").HasMaxLength(50).IsRequired();
            c.Property(x => x.Format).HasColumnName("Format").HasMaxLength(150).IsRequired();
            c.Property(x => x.MinimumDigits).HasColumnName("MinimumDigits");
            c.Property(x => x.ResetMode).HasColumnName("ResetMode");
            c.Property(x => x.StartingNumber).HasColumnName("StartingNumber");
        });
        b.HasMany(x => x.History).WithOne().HasForeignKey(x => new { x.TenantId, x.SeriesId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Cascade);
        b.HasMany(x => x.Defaults).WithOne().HasForeignKey(x => new { x.TenantId, x.SeriesId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Navigation(x => x.History).UsePropertyAccessMode(PropertyAccessMode.Field); b.Navigation(x => x.Defaults).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}

internal sealed class InvoiceNumberSeriesRevisionConfiguration : IEntityTypeConfiguration<InvoiceNumberSeriesRevision>
{
    public void Configure(EntityTypeBuilder<InvoiceNumberSeriesRevision> b)
    {
        b.ToTable("InvoiceNumberSeriesHistory", t =>
        {
            t.HasCheckConstraint("CK_InvoiceNumberSeriesHistory_Values", "[Revision] > 0 AND [Change] BETWEEN 0 AND 5 AND ([InvoiceKind] IS NULL OR [InvoiceKind] BETWEEN 0 AND 3)");
        });
        b.HasKey(x => new { x.TenantId, x.SeriesId, x.Revision }); b.Property(x => x.Revision).ValueGeneratedNever();
        SalesInvoiceJson.Map(b.Property(x => x.Configuration));
        b.Property(x => x.ChangedBy).HasMaxLength(450).IsRequired(); b.Property(x => x.ChangedAt).HasColumnType("datetimeoffset");
    }
}

internal sealed class InvoiceNumberSeriesDefaultConfiguration : IEntityTypeConfiguration<InvoiceNumberSeriesDefault>
{
    public void Configure(EntityTypeBuilder<InvoiceNumberSeriesDefault> b)
    {
        b.ToTable("InvoiceNumberSeriesDefaults", t => t.HasCheckConstraint("CK_InvoiceNumberSeriesDefaults_Kind", "[InvoiceKind] BETWEEN 0 AND 3"));
        b.HasKey(x => new { x.TenantId, x.InvoiceKind });
        b.Property(x => x.RowVersion).IsRowVersion();
    }
}
