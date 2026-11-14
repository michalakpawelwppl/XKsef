using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace xKsef.CredentialService.Data;

public sealed class CredentialsDbContextFactory : IDesignTimeDbContextFactory<CredentialsDbContext>
{
    public CredentialsDbContext CreateDbContext(string[] args)
    {
        // Design-time model generation only. Do not start the host or read runtime credentials.
        var options = new DbContextOptionsBuilder<CredentialsDbContext>()
            .UseSqlServer("Server=offline.invalid;Database=CredentialServiceModelOnly;Integrated Security=true;Connect Timeout=1")
            .Options;
        return new CredentialsDbContext(options);
    }
}
