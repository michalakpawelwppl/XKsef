using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.Invoices;

namespace xKsef.Infrastructure.Persistence.Configurations;

internal sealed class StoredInvoiceConfiguration
    : IEntityTypeConfiguration<StoredInvoice>
{
    public void Configure(EntityTypeBuilder<StoredInvoice> builder)
    {
        builder.ToTable("invoices");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.KsefNumber)
            .IsRequired()
            .HasMaxLength(100);

        builder.HasIndex(x => x.KsefNumber)
            .IsUnique();

        builder.Property(x => x.ImportedAt)
            .IsRequired()
            .HasColumnType("datetimeoffset");

        builder.Property(x => x.XmlBytes)
            .HasField("_xmlBytes")
            .UsePropertyAccessMode(PropertyAccessMode.Field)
            .IsRequired()
            .HasColumnType("varbinary(max)");
    }
}