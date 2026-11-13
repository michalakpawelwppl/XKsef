namespace xKsef.CredentialService.Contracts;

public sealed class AuthenticateKsefTokenRequest
{
    public Guid CredentialId { get; set; }

    public Guid TenantId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Nip { get; set; } = string.Empty;
}