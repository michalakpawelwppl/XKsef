using System.ComponentModel.DataAnnotations;

namespace xKsef.Contracts.Authentication;

public sealed class RegisterRequest
{
    [Required, StringLength(256)]
    public required string Login { get; init; }
    [Required, StringLength(256)]
    public required string Email { get; init; }
    [Required, StringLength(256), DataType(DataType.Password)]
    public required string Password { get; init; }
}