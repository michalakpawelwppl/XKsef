using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.Contractors;

namespace xKsef.Infrastructure.Persistence.Configurations;

public sealed class AddressesDataConfiguration : IEntityTypeConfiguration<AddressesData>
{
    public void Configure(EntityTypeBuilder<AddressesData> builder)
    {
        builder.ToTable("AddressesData");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.CountryCode).IsRequired().HasMaxLength(2);
        builder.Property(x => x.Street).IsRequired().HasMaxLength(200);
        builder.Property(x => x.BuildingNumber).IsRequired().HasMaxLength(20);
        builder.Property(x => x.ApartmentNumber).HasMaxLength(20);
        builder.Property(x => x.PostalCode).IsRequired().HasMaxLength(20);
        builder.Property(x => x.City).IsRequired().HasMaxLength(200);

        builder.HasIndex(x => x.AddressId).IsUnique();

        builder.HasOne<Address>().WithOne().HasForeignKey<AddressesData>(x => x.AddressId).OnDelete(DeleteBehavior.Restrict);
    }
}