using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace xKsef.Infrastructure.Persistence;

public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        // Design-time model generation only: never start the API or load runtime connections.
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=offline.invalid;Database=AppModelOnly;Integrated Security=true;Connect Timeout=1").Options);
    }
}
