namespace xKsef.Domain.Ksef.Authentication;

public sealed class KsefCredential
{
    public Guid Id { get; set; }

    public string Name { get; set; } = string.Empty;

    public KsefCredentialType Type { get; set; }

    public string? UserId { get; set; }

    public bool IsActive { get; set; } = true;
}