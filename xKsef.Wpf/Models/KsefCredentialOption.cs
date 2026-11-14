namespace xKsef.Wpf.Models;

public sealed class KsefCredentialOption
{
    public Guid CredentialId { get; set; }
    public string CredentialType { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
}