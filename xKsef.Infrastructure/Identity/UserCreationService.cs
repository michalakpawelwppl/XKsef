using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Identity;
using xKsef.Application.Authentication;

namespace xKsef.Infrastructure.Identity;

internal sealed class UserCreationService : IUserCreationService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserCreationService(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<CreateUserResult> CreateAsync(string login, string email, string password, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var user = new ApplicationUser { UserName = login, Email = email, LockoutEnabled = true };
        IdentityResult result;
        try
        {
            result = await _userManager.CreateAsync(user, password);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 }
            && ex.Entries.All(entry => entry.Entity is ApplicationUser))
        {
            // A concurrent registration can pass Identity validation before the unique index rejects its INSERT.
            // Do not retry or expose SQL details, submitted values or index names.
            foreach (var entry in ex.Entries) entry.State = EntityState.Detached;
            return new CreateUserResult(null, new[] { "DuplicateAccount" });
        }

        // Return codes only: Identity descriptions can include submitted account data.
        return result.Succeeded
            ? new CreateUserResult(user.Id, Array.Empty<string>())
            : new CreateUserResult(null, result.Errors.Select(error => error.Code).Distinct().ToArray());
    }
}
