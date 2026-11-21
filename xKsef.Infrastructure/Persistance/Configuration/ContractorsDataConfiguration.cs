using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.Contractors;

namespace xKsef.Infrastructure.Persistence.Configurations;

public sealed class ContractorsDataConfiguration : IEntityTypeConfiguration<ContractorsData>
{
    public void Configure(EntityTypeBuilder<ContractorsData> builder)
    {
        builder.ToTable("ContractorsData", table =>
        {
            table.HasCheckConstraint("CK_ContractorsData_FullName_NotEmpty", "LEN(LTRIM(RTRIM([FullName]))) > 0");
            table.HasCheckConstraint("CK_ContractorsData_ShortName_NotEmpty", "LEN(LTRIM(RTRIM([ShortName]))) > 0");
            table.HasCheckConstraint("CK_ContractorsData_EuVatRegistration", "([IsEuVatRegistered] = 0 AND [EuVatNumber] IS NULL) OR ([IsEuVatRegistered] = 1 AND [EuVatNumber] IS NOT NULL AND LEN(LTRIM(RTRIM([EuVatNumber]))) > 0)");
        });

        builder.HasKey(x => x.Id);

        builder.Property(x => x.FullName).IsRequired().HasMaxLength(500);
        builder.Property(x => x.ShortName).IsRequired().HasMaxLength(100);
        builder.Property(x => x.Nip).HasMaxLength(20);
        builder.Property(x => x.Regon).HasMaxLength(14);
        builder.Property(x => x.KrsNumber).HasMaxLength(10);
        builder.Property(x => x.EuVatNumber).HasMaxLength(32);
        builder.Property(x => x.OtherTaxNumber).HasMaxLength(100);
        builder.Property(x => x.IsEuVatRegistered).IsRequired().HasDefaultValue(false);

        builder.HasIndex(x => x.ContractorId).IsUnique();

        builder.HasOne<Contractor>().WithOne().HasForeignKey<ContractorsData>(x => x.ContractorId).OnDelete(DeleteBehavior.Restrict);
    }
}
