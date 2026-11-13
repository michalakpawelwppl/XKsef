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
        IdentityResult result = await _userManager.CreateAsync(user, password);

        // Return codes only: Identity descriptions can include submitted account data.
        return result.Succeeded
            ? new CreateUserResult(user.Id, Array.Empty<string>())
            : new CreateUserResult(null, result.Errors.Select(error => error.Code).Distinct().ToArray());
    }
}
