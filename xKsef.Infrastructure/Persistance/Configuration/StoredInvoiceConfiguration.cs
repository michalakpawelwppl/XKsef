using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.Invoices;
using xKsef.Domain.Tenants;

namespace xKsef.Infrastructure.Persistence.Configurations;

internal sealed class StoredInvoiceConfiguration : IEntityTypeConfiguration<StoredInvoice>
{
    public void Configure(EntityTypeBuilder<StoredInvoice> builder)
    {
        builder.ToTable("invoices");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.KsefNumber).IsRequired().HasMaxLength(100);

        builder.Property(x => x.TenantId).IsRequired();
        builder.HasIndex(x => new { x.TenantId, x.KsefNumber }).IsUnique();
        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Restrict);

        builder.Property(x => x.ImportedAt).IsRequired().HasColumnType("datetimeoffset");
        builder.Property(x => x.InvoiceNumber).HasMaxLength(100).IsRequired();

        builder.Property(x => x.IssueDate).HasColumnType("date").IsRequired();

        builder.Property(x => x.Direction).HasConversion<int>().IsRequired();

        builder.Property(x => x.GrossAmount).HasPrecision(18, 2).IsRequired();
        builder.Property(x => x.NetAmount).HasPrecision(18, 2);
        builder.Property(x => x.VatAmount).HasPrecision(18, 2);
        builder.Property(x => x.VatAmountPln).HasPrecision(18, 2);
        builder.Property(x => x.InvoicingDate).HasColumnType("datetimeoffset");
        builder.Property(x => x.BuyerNip).HasMaxLength(10);
        builder.Property(x => x.BuyerName).HasMaxLength(512);

        builder.Property(x => x.Currency).HasMaxLength(3).IsRequired();

        builder.Property(x => x.SellerName).HasMaxLength(250).IsRequired();

        builder.Property(x => x.SellerNip).HasMaxLength(20).IsRequired();
        builder.Property(x => x.XmlBytes).HasField("_xmlBytes").UsePropertyAccessMode(PropertyAccessMode.Field).IsRequired().HasColumnType("varbinary(max)");
    }
}