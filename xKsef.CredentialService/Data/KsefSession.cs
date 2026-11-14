namespace xKsef.CredentialService.Data;

public sealed class KsefSession
{
    public Guid Id { get; set; }

    public string UserId { get; set; } = string.Empty;

    public Guid TenantId { get; set; }

    public Guid CredentialId { get; set; }

    public KsefCredential Credential { get; set; } = null!;

    public string ContextNip { get; set; } = string.Empty;

    // The encrypted payload will contain both AccessToken and RefreshToken.
    public byte[] EncryptedData { get; set; } = [];

    public byte[] EncryptedDek { get; set; } = [];

    public byte[] Nonce { get; set; } = [];

    public byte[] AuthTag { get; set; } = [];

    public string ProtectionType { get; set; } = "DPAPI-CurrentUser";

    public DateTimeOffset AccessTokenValidUntil { get; set; }

    public DateTimeOffset RefreshTokenValidUntil { get; set; }

    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}
