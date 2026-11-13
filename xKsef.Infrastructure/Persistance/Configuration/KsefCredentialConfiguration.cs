using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.Ksef.Authentication;

namespace xKsef.Infrastructure.Persistence.Configurations;

public sealed class KsefCredentialConfiguration : IEntityTypeConfiguration<KsefCredential>
{
    public void Configure(EntityTypeBuilder<KsefCredential> builder)
    {
        builder.ToTable("KsefCredentials");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.Name).HasMaxLength(200).IsRequired();

        builder.Property(x => x.Type).HasConversion<int>().IsRequired();

        builder.Property(x => x.UserId).HasMaxLength(450);

        builder.Property(x => x.IsActive).IsRequired();

        builder.HasIndex(x => x.UserId);
    }
}