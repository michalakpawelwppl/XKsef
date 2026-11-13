namespace xKsef.CredentialService.Services;

public sealed record StoredSessionToken(byte[] TokenBytes, DateTimeOffset ValidUntil);