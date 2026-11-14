using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Infrastructure.Identity;

namespace xKsef.Infrastructure.Persistence.Configurations;

internal sealed class ApplicationUserConfiguration : IEntityTypeConfiguration<ApplicationUser>
{
    public void Configure(EntityTypeBuilder<ApplicationUser> builder)
    {
        builder.HasIndex(user => user.NormalizedEmail).HasDatabaseName("EmailIndex").IsUnique().HasFilter("[NormalizedEmail] IS NOT NULL");
    }
}