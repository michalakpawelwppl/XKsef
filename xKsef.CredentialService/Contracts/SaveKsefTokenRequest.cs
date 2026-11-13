namespace xKsef.CredentialService.Contracts;

public sealed class SaveKsefTokenRequest
{
    public Guid TenantId { get; set; }
    public string UserId { get; set; } = string.Empty;
    public string Token { get; set; } = string.Empty;
}