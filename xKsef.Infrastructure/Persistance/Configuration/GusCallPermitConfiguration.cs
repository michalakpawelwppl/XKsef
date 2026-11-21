using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Infrastructure.Gus;

namespace xKsef.Infrastructure.Persistence.Configurations;

public sealed class GusCallPermitConfiguration : IEntityTypeConfiguration<GusCallPermitEntry>
{
    public void Configure(EntityTypeBuilder<GusCallPermitEntry> builder)
    {
        builder.ToTable("GusCallPermits", "dbo", table => table.HasCheckConstraint("CK_GusCallPermits_Environment", "[Environment] IN (0, 1)"));
        builder.HasKey(x => x.Id);
        builder.Property(x => x.Id).UseIdentityColumn();
        builder.Property(x => x.Environment).HasConversion<byte>().HasColumnType("tinyint").IsRequired();
        builder.Property(x => x.GrantedAtUtc).HasColumnType("datetime2(7)").IsRequired();
        builder.HasIndex(x => new { x.Environment, x.GrantedAtUtc });
    }
}
