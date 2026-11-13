using System.ComponentModel.DataAnnotations;

namespace xKsef.Api.Contracts.Users;

public sealed class CreateUserRequest
{
    [Required]
    [StringLength(256)]
    public required string Login { get; init; }

    [Required]
    [StringLength(256)]
    public required string Email { get; init; }

    [Required]
    [DataType(DataType.Password)]
    public required string Password { get; init; }
}
