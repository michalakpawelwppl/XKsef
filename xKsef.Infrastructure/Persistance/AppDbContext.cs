
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using xKsef.Domain.Invoices;
using xKsef.Infrastructure.Identity;

namespace xKsef.Infrastructure.Persistence;

public sealed class AppDbContext
    : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(
        DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public DbSet<StoredInvoice> Invoices => Set<StoredInvoice>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.ApplyConfigurationsFromAssembly(
            typeof(AppDbContext).Assembly);
    }
}