using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using xKsef.Domain.InvoiceNumberSeries;
using xKsef.Domain.SalesInvoices;
using NumberSeries = xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries;

namespace xKsef.Infrastructure.Persistence.Configurations;

internal sealed class InvoiceNumberCounterConfiguration : IEntityTypeConfiguration<InvoiceNumberCounter>
{
    public void Configure(EntityTypeBuilder<InvoiceNumberCounter> b)
    {
        b.ToTable("InvoiceNumberCounters", t => t.HasCheckConstraint("CK_InvoiceNumberCounters_Values", "[Period] BETWEEN 0 AND 9999 AND [StartingNumber] > 0 AND ([NextValue] IS NULL OR [NextValue] >= [StartingNumber])"));
        b.HasKey(x => new { x.TenantId, x.SeriesId, x.Period }); b.Property(x => x.Period).ValueGeneratedNever();
        b.HasOne<NumberSeries>().WithMany().HasForeignKey(x => new { x.TenantId, x.SeriesId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired(); b.Property(x => x.UpdatedBy).HasMaxLength(450).IsRequired();
        b.Property(x => x.CreatedAt).HasColumnType("datetimeoffset"); b.Property(x => x.UpdatedAt).HasColumnType("datetimeoffset");
    }
}

internal sealed class InvoiceNumberReservationConfiguration : IEntityTypeConfiguration<InvoiceNumberReservation>
{
    public void Configure(EntityTypeBuilder<InvoiceNumberReservation> b)
    {
        b.ToTable("InvoiceNumberReservations", t =>
        {
            t.HasCheckConstraint("CK_InvoiceNumberReservations_Values", "[NumericValue] > 0 AND [Period] BETWEEN 0 AND 9999 AND [State] BETWEEN 0 AND 3 AND (([State] = 2 AND [HoldsNumber] = 0) OR ([State] <> 2 AND [HoldsNumber] = 1))");
            t.HasCheckConstraint("CK_InvoiceNumberReservations_Resolution", "([State] IN (0,3) AND [EvidenceId] IS NULL AND [KsefNumber] IS NULL AND [ResolvedAt] IS NULL AND [ResolvedBy] IS NULL AND [ConfirmedAt] IS NULL) OR ([State] IN (1,2) AND [EvidenceId] IS NOT NULL AND [ResolvedAt] IS NOT NULL AND [ResolvedBy] IS NOT NULL AND [ConfirmedAt] IS NOT NULL AND (([State] = 1 AND [KsefNumber] IS NOT NULL) OR ([State] = 2 AND [KsefNumber] IS NULL)))");
        });
        b.HasKey(x => x.Id); b.Property(x => x.Id).ValueGeneratedNever();
        b.HasAlternateKey(x => new { x.TenantId, x.InvoiceId, x.Id });
        b.HasOne<SalesInvoice>().WithMany().HasForeignKey(x => new { x.TenantId, x.InvoiceId }).HasPrincipalKey(x => new { x.TenantId, x.Id }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<SalesInvoiceApproval>().WithMany().HasForeignKey(x => new { x.TenantId, x.InvoiceId, x.ApprovalId, x.ApprovedRevision }).HasPrincipalKey(x => new { x.TenantId, x.SalesInvoiceId, x.Id, x.ApprovedRevision }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InvoiceNumberCounter>().WithMany().HasForeignKey(x => new { x.TenantId, x.SeriesId, x.Period }).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<InvoiceNumberSeriesRevision>().WithMany().HasForeignKey(x => new { x.TenantId, x.SeriesId, x.SeriesRevision }).OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.FullNumber).HasMaxLength(100).IsRequired(); b.Property(x => x.NormalizedNumber).HasMaxLength(100).UseCollation("Latin1_General_100_BIN2").IsRequired();
        b.Property(x => x.InvoiceVersion).HasMaxLength(12).IsRequired(); b.Property(x => x.RowVersion).IsRowVersion();
        b.Property(x => x.CreatedBy).HasMaxLength(450).IsRequired(); b.Property(x => x.BlockedBy).HasMaxLength(450); b.Property(x => x.ResolvedBy).HasMaxLength(450);
        b.Property(x => x.UncertaintyReason).HasMaxLength(1000); b.Property(x => x.Decision).HasMaxLength(2000).IsRequired();
        b.Property(x => x.EvidenceId).HasMaxLength(256); b.Property(x => x.KsefNumber).HasMaxLength(100); b.Property(x => x.IssueDate).HasColumnType("date");
        foreach (var property in new[] { nameof(InvoiceNumberReservation.CreatedAt), nameof(InvoiceNumberReservation.BlockedAt), nameof(InvoiceNumberReservation.ResolvedAt), nameof(InvoiceNumberReservation.ConfirmedAt) }) b.Property(property).HasColumnType("datetimeoffset");
        b.HasIndex(x => new { x.TenantId, x.OperationId }).IsUnique();
        b.HasIndex(x => new { x.TenantId, x.InvoiceId }).IsUnique().HasFilter("[HoldsNumber] = 1");
        b.HasIndex(x => new { x.TenantId, x.SeriesId, x.Period, x.NumericValue }).IsUnique().HasFilter("[HoldsNumber] = 1");
        b.HasIndex(x => new { x.TenantId, x.NormalizedNumber }).IsUnique().HasFilter("[HoldsNumber] = 1");
    }
}
