namespace xKsef.Application.Authentication;

public sealed record AuthenticatedUser(
    string Id,
    string UserName, string? SecurityStamp = null);