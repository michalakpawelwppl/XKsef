using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Infrastructure.Authentication;
using xKsef.Infrastructure.Identity;

namespace xKsef.Infrastructure.Persistence.Configurations;

internal sealed class BrowserSessionConfiguration : IEntityTypeConfiguration<BrowserSession>
{
    public void Configure(EntityTypeBuilder<BrowserSession> builder)
    {
        builder.ToTable("BrowserSessions", table =>
        {
            table.HasCheckConstraint("CK_BrowserSessions_Lifetime", "[CreatedAt] <= [RenewedAt] AND [RenewedAt] < [IdleExpiresAt] AND [IdleExpiresAt] <= [AbsoluteExpiresAt]");
            table.HasCheckConstraint("CK_BrowserSessions_Version", "[Version] <> '00000000-0000-0000-0000-000000000000'");
            table.HasCheckConstraint("CK_BrowserSessions_Payload", "DATALENGTH([ProtectedTicket]) > 0");
        });
        builder.HasKey(session => session.IdHash);
        builder.Property(session => session.IdHash).HasMaxLength(64).IsUnicode(false).UseCollation("Latin1_General_100_BIN2");
        builder.Property(session => session.UserId).HasMaxLength(450).IsRequired();
        builder.Property(session => session.ProtectedTicket).IsRequired();
        builder.Property(session => session.Version).IsConcurrencyToken();
        builder.Property(session => session.CreatedAt).HasColumnType("datetimeoffset");
        builder.Property(session => session.RenewedAt).HasColumnType("datetimeoffset");
        builder.Property(session => session.IdleExpiresAt).HasColumnType("datetimeoffset");
        builder.Property(session => session.AbsoluteExpiresAt).HasColumnType("datetimeoffset");
        builder.Property(session => session.RevokedAt).HasColumnType("datetimeoffset");
        builder.HasOne<ApplicationUser>().WithMany().HasForeignKey(session => session.UserId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(session => new { session.UserId, session.RevokedAt });
        builder.HasIndex(session => session.IdleExpiresAt);
        builder.HasIndex(session => session.AbsoluteExpiresAt);
    }
}