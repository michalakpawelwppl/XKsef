using Microsoft.EntityFrameworkCore;

namespace xKsef.CredentialService.Data;

public sealed class CredentialsDbContext : DbContext
{
    public CredentialsDbContext(DbContextOptions<CredentialsDbContext> options) : base(options)
    {
    }

    public DbSet<KsefCredential> KsefCredentials => Set<KsefCredential>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<KsefCredential>(entity =>
        {
            entity.ToTable("KsefCredentials");

            entity.HasKey(x => x.Id);

            entity.Property(x => x.UserId).HasMaxLength(450).IsRequired();
            entity.Property(x => x.CredentialType).HasMaxLength(30).IsRequired();
            entity.Property(x => x.ProtectionType).HasMaxLength(50).IsRequired();

            entity.Property(x => x.EncryptedData).IsRequired();
            entity.Property(x => x.EncryptedDek).IsRequired();
            entity.Property(x => x.Nonce).IsRequired();
            entity.Property(x => x.AuthTag).IsRequired();
            entity.Property(x => x.SourceCredentialId);
            entity.Property(x => x.ContextNip).HasMaxLength(10);

            entity.HasIndex(x => new { x.TenantId, x.UserId, x.CredentialType, x.SourceCredentialId, x.ContextNip }).IsUnique().HasFilter("[CredentialType] IN ('AccessToken', 'RefreshToken')");
        });
    }
}