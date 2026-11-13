namespace xKsef.Application.Credentials;

public sealed record KsefAccessTokenResponse(string AccessToken, DateTimeOffset ValidUntil);