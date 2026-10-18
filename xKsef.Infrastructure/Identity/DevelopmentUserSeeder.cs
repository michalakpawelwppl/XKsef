using Microsoft.AspNetCore.Identity;

namespace xKsef.Infrastructure.Identity;

public sealed class DevelopmentUserSeeder
{
    private readonly UserManager<ApplicationUser> _userManager;

    public DevelopmentUserSeeder(
        UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    public async Task SeedAsync(string password)
    {
        const string userName = "testadmin";

        var existingUser = await _userManager.FindByNameAsync(userName);

        if (existingUser is not null)
        {
            return;
        }

        var user = new ApplicationUser
        {
            UserName = userName,
            Email = "testadmin@example.com",
            EmailConfirmed = true,
            LockoutEnabled = true
        };

        var result = await _userManager.CreateAsync(user, password);

        if (!result.Succeeded)
        {
            var errors = string.Join(
                "; ",
                result.Errors.Select(e => e.Description));

            throw new InvalidOperationException(
                $"Nie udało się utworzyć użytkownika testowego: {errors}");
        }
    }
}