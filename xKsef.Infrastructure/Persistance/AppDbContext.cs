using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using xKsef.Domain.Contractors;
using xKsef.Domain.Invoices;
using xKsef.Domain.Ksef.Authentication;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Authentication;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Gus;
using xKsef.Domain.SalesInvoices;

namespace xKsef.Infrastructure.Persistence;

public sealed class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options): base(options)
    {
    }

    public DbSet<BrowserSession> BrowserSessions => Set<BrowserSession>();
    public DbSet<GusCallPermitEntry> GusCallPermits => Set<GusCallPermitEntry>();

    public DbSet<StoredInvoice> Invoices => Set<StoredInvoice>();
    public DbSet<SalesInvoice> SalesInvoices => Set<SalesInvoice>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<TaxEntity> TaxEntities => Set<TaxEntity>();
    public DbSet<KsefCredential> KsefCredentials => Set<KsefCredential>();

    public DbSet<KsefAuthenticationContext> KsefAuthenticationContexts => Set<KsefAuthenticationContext>();

    public DbSet<Contractor> Contractors => Set<Contractor>();
    public DbSet<ContractorsData> ContractorsData => Set<ContractorsData>();
    public DbSet<Address> Addresses => Set<Address>();
    public DbSet<AddressesData> AddressesData => Set<AddressesData>();

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        GuardApprovalHistory();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        GuardApprovalHistory();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void GuardApprovalHistory()
    {
        ChangeTracker.DetectChanges();
        if (ChangeTracker.Entries<SalesInvoiceApproval>().Any(x => x.State == EntityState.Modified || x.State == EntityState.Deleted
            && !ChangeTracker.Entries<SalesInvoice>().Any(invoice => invoice.Entity.TenantId == x.Entity.TenantId && invoice.Entity.Id == x.Entity.SalesInvoiceId
                && invoice.State == EntityState.Modified && invoice.OriginalValues.GetValue<SalesInvoiceStatus>(nameof(SalesInvoice.Status)) == SalesInvoiceStatus.Approved
                && invoice.Entity.Status == SalesInvoiceStatus.Draft && invoice.Entity.Approvals.Count == 0)))
            throw new InvalidOperationException("Zatwierdzony snapshot jest niezmienny; usunięcie wymaga cofnięcia zatwierdzenia.");
    }
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
        if (Database.IsSqlServer())
        {
            modelBuilder.Entity<SalesInvoice>().ToTable(table => table.HasCheckConstraint("CK_SalesInvoices_Fa3Json", "ISJSON([Fa3]) = 1 AND JSON_VALUE([Fa3], '$.Version') IS NOT NULL AND JSON_VALUE([Fa3], '$.Version') = '1'"));
            modelBuilder.Entity<SalesInvoiceLine>().ToTable(table => table.HasCheckConstraint("CK_SalesInvoiceLines_Annex15Json", "ISJSON([Annex15]) = 1"));
            modelBuilder.Entity<xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeriesRevision>().ToTable(table => table.HasCheckConstraint("CK_InvoiceNumberSeriesHistory_Json", "ISJSON([Configuration]) = 1"));
            modelBuilder.Entity<SalesInvoiceApproval>().ToTable(table => table.HasCheckConstraint("CK_SalesInvoiceApprovals_Snapshot", "ISJSON([SnapshotJson]) = 1"));
            foreach (var entity in new[] { typeof(SalesInvoicePartySnapshot), typeof(SalesInvoiceDetails), typeof(SalesInvoiceParticipant) })
                modelBuilder.Entity(entity).ToTable(table => table.HasCheckConstraint("CK_" + entity.Name + "_Json", "ISJSON([Data]) = 1"));
        }
    }
}
