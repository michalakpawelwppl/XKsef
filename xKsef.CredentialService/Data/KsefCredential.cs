namespace xKsef.CredentialService.Data;

public sealed class KsefCredential
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;

    public Guid? TenantId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string CredentialType { get; set; } = string.Empty;

    public byte[] EncryptedData { get; set; } = [];

    public byte[] EncryptedDek { get; set; } = [];

    public byte[] Nonce { get; set; } = [];

    public byte[] AuthTag { get; set; } = [];

    public string ProtectionType { get; set; } = "DPAPI-CurrentUser";

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    

    public string? ContextNip { get; set; }
}