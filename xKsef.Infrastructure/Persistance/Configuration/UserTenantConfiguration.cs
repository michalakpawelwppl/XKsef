using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Identity;

namespace xKsef.Infrastructure.Persistence.Configurations;

public sealed class UserTenantConfiguration : IEntityTypeConfiguration<UserTenant>
{
    public void Configure(EntityTypeBuilder<UserTenant> builder)
    {
        builder.ToTable("UserTenants");

        builder.HasKey(x => new { x.UserId, x.TenantId });

        builder.Property(x => x.UserId).IsRequired().HasMaxLength(450);

        builder.Property(x => x.Role).IsRequired().HasMaxLength(50);
        builder.Property(x => x.PreferredKsefCredentialId).IsRequired(false);

        builder.HasOne<Tenant>().WithMany().HasForeignKey(x => x.TenantId).OnDelete(DeleteBehavior.Cascade);

        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
    }
}