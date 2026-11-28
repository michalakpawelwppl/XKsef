using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.SalesInvoices;

namespace xKsef.Infrastructure.Persistence.Configurations;

internal sealed class SalesInvoiceParticipantConfiguration : IEntityTypeConfiguration<SalesInvoiceParticipant>
{
    public void Configure(EntityTypeBuilder<SalesInvoiceParticipant> b)
    {
        b.ToTable("SalesInvoiceParticipants", table =>
        {
            table.HasCheckConstraint("CK_SalesInvoiceParticipants_Ordinal", "[Ordinal] > 0");
            table.HasCheckConstraint("CK_SalesInvoiceParticipants_Role", "[Role] IS NULL OR [Role] BETWEEN 0 AND 11");
            table.HasCheckConstraint("CK_SalesInvoiceParticipants_Share", "[SharePercent] IS NULL OR [SharePercent] BETWEEN 0 AND 100");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.TenantId, x.SalesInvoiceId, x.Id });
        b.HasIndex(x => new { x.TenantId, x.SalesInvoiceId, x.Ordinal }).IsUnique();
        b.Property(x => x.SharePercent).HasPrecision(9, 6); b.Property(x => x.OtherRoleDescription).HasMaxLength(256);
        SalesInvoiceJson.Map(b.Property(x => x.Data));
    }
}
