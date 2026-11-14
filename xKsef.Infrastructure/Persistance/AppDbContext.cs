using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using xKsef.Domain.Invoices;
using xKsef.Domain.Ksef.Authentication;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Authentication;

namespace xKsef.Infrastructure.Persistence;

public sealed class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options): base(options)
    {
    }

    public DbSet<BrowserSession> BrowserSessions => Set<BrowserSession>();

    public DbSet<StoredInvoice> Invoices => Set<StoredInvoice>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<UserTenant> UserTenants => Set<UserTenant>();
    public DbSet<TaxEntity> TaxEntities => Set<TaxEntity>();
    public DbSet<KsefCredential> KsefCredentials => Set<KsefCredential>();

    public DbSet<KsefAuthenticationContext> KsefAuthenticationContexts => Set<KsefAuthenticationContext>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
    }
}