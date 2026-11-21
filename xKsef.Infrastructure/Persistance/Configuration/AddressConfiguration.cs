using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.Contractors;

namespace xKsef.Infrastructure.Persistence.Configurations;

public sealed class AddressConfiguration : IEntityTypeConfiguration<Address>
{
    public void Configure(EntityTypeBuilder<Address> builder)
    {
        builder.ToTable("Addresses", table => table.HasCheckConstraint("CK_Addresses_AddressType", "[AddressType] IN (1)"));

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AddressType).IsRequired().HasConversion<int>();

        builder.HasIndex(x => new { x.ContractorsDataId, x.AddressType }).IsUnique();

        builder.HasOne<ContractorsData>().WithMany().HasForeignKey(x => x.ContractorsDataId).OnDelete(DeleteBehavior.Restrict);
    }
}
