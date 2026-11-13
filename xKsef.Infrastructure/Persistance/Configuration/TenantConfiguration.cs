using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.Tenants;

namespace xKsef.Infrastructure.Persistence.Configurations;

public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("Tenants");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).IsRequired().HasMaxLength(200);

        builder.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);
        
        builder.HasOne(x => x.TaxEntity).WithMany().HasForeignKey(x => x.TaxEntityId).OnDelete(DeleteBehavior.Restrict);
    }
}