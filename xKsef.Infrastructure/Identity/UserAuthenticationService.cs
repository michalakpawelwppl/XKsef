using Microsoft.AspNetCore.Identity;
using xKsef.Application.Authentication;

namespace xKsef.Infrastructure.Identity;

internal sealed class UserAuthenticationService
    : IUserAuthenticationService
{
    private readonly UserManager<ApplicationUser> _userManager;

    public UserAuthenticationService(
        UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task<AuthenticatedUser?> AuthenticateAsync(
        string login,
        string password,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(login) ||
            string.IsNullOrEmpty(password))
        {
            return null;
        }

        var user = await _userManager.FindByNameAsync(login);

        cancellationToken.ThrowIfCancellationRequested();

        if (user is null)
        {
            return null;
        }

        if (await _userManager.IsLockedOutAsync(user))
        {
            return null;
        }

        var isPasswordValid = await _userManager.CheckPasswordAsync(
            user,
            password);

        if (!isPasswordValid)
        {
            var result = await _userManager.AccessFailedAsync(user);

            if (!result.Succeeded)
            {
                throw new InvalidOperationException(
                    "Nie udało się zarejestrować nieudanej próby logowania.");
            }

            return null;
        }
        var resetResult = await _userManager.ResetAccessFailedCountAsync(user);

        if (!resetResult.Succeeded)
        {
            throw new InvalidOperationException(
                "Nie udało się zresetować licznika nieudanych logowań.");
        }

        return new AuthenticatedUser(
     user.Id,
     user.UserName!, user.SecurityStamp);
    }
}