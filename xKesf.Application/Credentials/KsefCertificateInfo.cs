namespace xKsef.Application.Credentials;

public sealed record KsefCertificateInfo(Guid CredentialId, string Name, DateTimeOffset CreatedAt);
