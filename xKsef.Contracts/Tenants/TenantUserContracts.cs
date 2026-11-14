using System.ComponentModel.DataAnnotations;

namespace xKsef.Contracts.Tenants;

public static class TenantRoles
{
    public const string Admin = "Admin";
    public const string User = "User";
    public const string Pattern = @"\A(?:Admin|User)\z";
    public static bool IsValid(string? role) => role is Admin or User;
}

public sealed record TenantUserDto(string UserId, string Login, string? Email, string Role);
public sealed record TenantAccessChangeDto(string? CurrentUserRole);

public sealed class AddTenantUserRequest
{
    [Required, StringLength(256)]
    public required string Identifier { get; init; }
    [Required, RegularExpression(TenantRoles.Pattern)]
    public required string Role { get; init; }
}

public sealed class ChangeTenantUserRoleRequest
{
    [Required, RegularExpression(TenantRoles.Pattern)]
    public required string Role { get; init; }
}