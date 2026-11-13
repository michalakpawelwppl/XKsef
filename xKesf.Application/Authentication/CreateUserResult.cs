namespace xKsef.Application.Authentication;

public sealed record CreateUserResult(string? UserId, IReadOnlyList<string> Errors);
