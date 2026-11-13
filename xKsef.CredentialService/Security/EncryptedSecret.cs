namespace xKsef.CredentialService.Security;

public sealed class EncryptedSecret
{
    public byte[] EncryptedData { get; set; } = [];
    public byte[] EncryptedDek { get; set; } = [];
    public byte[] Nonce { get; set; } = [];
    public byte[] AuthTag { get; set; } = [];
    public string ProtectionType { get; set; } = "DPAPI-CurrentUser";
}