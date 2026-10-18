using System.ComponentModel.DataAnnotations;

namespace xKsef.Api.Contracts.Authentication;

public sealed class LoginRequest
{
    [Required]
    public required string Login { get; init; }

    [Required]
    public required string Password { get; init; }
}