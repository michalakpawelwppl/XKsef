using Microsoft.EntityFrameworkCore;

namespace xKsef.CredentialService.Data;

public sealed class CredentialsDbContext : DbContext
{
    public CredentialsDbContext(DbContextOptions<CredentialsDbContext> options) : base(options)
    {
    }

    public DbSet<KsefCredential> KsefCredentials => Set<KsefCredential>();

    public DbSet<KsefSession> KsefSessions => Set<KsefSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<KsefCredential>(entity =>
        {
            entity.ToTable("KsefCredentials", table =>
            {
                table.HasCheckConstraint("CK_KsefCredentials_Id", "[Id] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint("CK_KsefCredentials_UserId", "LEN(LTRIM(RTRIM([UserId]))) > 0");
                table.HasCheckConstraint("CK_KsefCredentials_SourceContext",
                    "([CredentialType] COLLATE Latin1_General_100_BIN2 = N'Certificate' AND [TenantId] IS NULL AND [ContextNip] IS NULL) OR " +
                    "([CredentialType] COLLATE Latin1_General_100_BIN2 = N'Token' AND [TenantId] IS NOT NULL AND [TenantId] <> '00000000-0000-0000-0000-000000000000' AND " +
                    "[ContextNip] IS NOT NULL AND DATALENGTH([ContextNip]) = 20 AND [ContextNip] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%')");
                table.HasCheckConstraint("CK_KsefCredentials_EncryptedPayload", "DATALENGTH([EncryptedData]) > 0 AND DATALENGTH([EncryptedDek]) > 0");
                table.HasCheckConstraint("CK_KsefCredentials_Nonce", "DATALENGTH([Nonce]) = 12");
                table.HasCheckConstraint("CK_KsefCredentials_AuthTag", "DATALENGTH([AuthTag]) = 16");
                table.HasCheckConstraint("CK_KsefCredentials_ProtectionType", "[ProtectionType] COLLATE Latin1_General_100_BIN2 = N'DPAPI-CurrentUser'");
            });

            entity.HasKey(x => x.Id);
            entity.HasAlternateKey(x => new { x.Id, x.UserId });

            entity.Property(x => x.Name).HasMaxLength(200).IsRequired().HasDefaultValue(string.Empty);
            entity.Property(x => x.IsActive).IsRequired().HasDefaultValue(true);

            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.CredentialType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ProtectionType).HasMaxLength(50).IsRequired();

            entity.Property(x => x.EncryptedData).IsRequired();
            entity.Property(x => x.EncryptedDek).IsRequired();
            entity.Property(x => x.Nonce).HasMaxLength(12).IsRequired();
            entity.Property(x => x.AuthTag).HasMaxLength(16).IsRequired();
            entity.Property(x => x.TenantId).IsRequired(false);
            entity.Property(x => x.ContextNip).HasMaxLength(10);

            entity.HasIndex(x => new { x.UserId, x.CredentialType, x.IsActive, x.CreatedAt });
            entity.HasIndex(x => new { x.UserId, x.TenantId, x.ContextNip, x.CredentialType, x.IsActive, x.CreatedAt });
        });

        modelBuilder.Entity<KsefSession>(entity =>
        {
            entity.ToTable("KsefSessions", table =>
            {
                table.HasCheckConstraint("CK_KsefSessions_Identifiers", "[Id] <> '00000000-0000-0000-0000-000000000000' AND [TenantId] <> '00000000-0000-0000-0000-000000000000' AND [CredentialId] <> '00000000-0000-0000-0000-000000000000'");
                table.HasCheckConstraint("CK_KsefSessions_UserId", "LEN(LTRIM(RTRIM([UserId]))) > 0");
                table.HasCheckConstraint("CK_KsefSessions_ContextNip", "DATALENGTH([ContextNip]) = 20 AND [ContextNip] COLLATE Latin1_General_100_BIN2 NOT LIKE N'%[^0-9]%'");
                table.HasCheckConstraint("CK_KsefSessions_EncryptedPayload", "DATALENGTH([EncryptedData]) > 0 AND DATALENGTH([EncryptedDek]) > 0");
                table.HasCheckConstraint("CK_KsefSessions_Nonce", "DATALENGTH([Nonce]) = 12");
                table.HasCheckConstraint("CK_KsefSessions_AuthTag", "DATALENGTH([AuthTag]) = 16");
                table.HasCheckConstraint("CK_KsefSessions_ProtectionType", "LEN(LTRIM(RTRIM([ProtectionType]))) > 0");
            });

            entity.HasKey(x => x.Id);
            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.ContextNip).HasMaxLength(10).IsRequired();
            entity.Property(x => x.EncryptedData).IsRequired();
            entity.Property(x => x.EncryptedDek).IsRequired();
            entity.Property(x => x.Nonce).HasMaxLength(12).IsRequired();
            entity.Property(x => x.AuthTag).HasMaxLength(16).IsRequired();
            entity.Property(x => x.ProtectionType).HasMaxLength(50).IsRequired();
            entity.Property(x => x.AccessTokenValidUntil).HasColumnType("datetimeoffset").IsRequired();
            entity.Property(x => x.RefreshTokenValidUntil).HasColumnType("datetimeoffset").IsRequired();
            entity.Property(x => x.CreatedAt).HasColumnType("datetimeoffset").IsRequired();

            entity.HasOne(x => x.Credential).WithMany()
                .HasForeignKey(x => new { x.CredentialId, x.UserId })
                .HasPrincipalKey(x => new { x.Id, x.UserId })
                .OnDelete(DeleteBehavior.Restrict);
            entity.HasIndex(x => new { x.UserId, x.TenantId, x.CredentialId, x.ContextNip }).IsUnique();
        });
    }
}