namespace xKsef.Infrastructure.Ksef.Authentication;

public sealed record KsefTokens(
    string AccessToken,
    DateTimeOffset AccessTokenValidUntil,
    string RefreshToken,
    DateTimeOffset RefreshTokenValidUntil);