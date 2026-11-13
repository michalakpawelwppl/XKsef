namespace xKsef.Api.Contracts.CredentialService;

public sealed record KsefAccessTokenResponse(string AccessToken, DateTimeOffset ValidUntil);