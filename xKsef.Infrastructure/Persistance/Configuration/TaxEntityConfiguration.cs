using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.Tenants;

namespace xKsef.Infrastructure.Persistence.Configurations;

public sealed class TaxEntityConfiguration : IEntityTypeConfiguration<TaxEntity>
{
    public void Configure(EntityTypeBuilder<TaxEntity> builder)
    {
        builder.ToTable("TaxEntities");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Nip).HasMaxLength(10).IsRequired();
        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();
        builder.HasIndex(x => x.Nip).IsUnique();
    }
}