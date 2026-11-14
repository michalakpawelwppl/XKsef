namespace xKsef.CredentialService.Contracts;

public sealed record KsefCertificateInfo(Guid CredentialId, string Name, DateTimeOffset CreatedAt);
