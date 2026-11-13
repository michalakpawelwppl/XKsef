using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.Ksef.Authentication;

namespace xKsef.Infrastructure.Persistence.Configurations;

public sealed class KsefAuthenticationContextConfiguration : IEntityTypeConfiguration<KsefAuthenticationContext>
{
    public void Configure(EntityTypeBuilder<KsefAuthenticationContext> builder)
    {
        builder.ToTable("KsefAuthenticationContexts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.IsActive).IsRequired();

        builder.HasOne(x => x.Credential).WithMany().HasForeignKey(x => x.CredentialId).OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.TaxEntity).WithMany().HasForeignKey(x => x.TaxEntityId).OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(x => new { x.CredentialId, x.TaxEntityId }).IsUnique();
    }
}