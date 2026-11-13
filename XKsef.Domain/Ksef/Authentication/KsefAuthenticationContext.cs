using xKsef.Domain.Tenants;

namespace xKsef.Domain.Ksef.Authentication;

public sealed class KsefAuthenticationContext
{
    public Guid Id { get; set; }

    public Guid CredentialId { get; set; }

    public KsefCredential Credential { get; set; } = null!;

    public Guid TaxEntityId { get; set; }

    public TaxEntity TaxEntity { get; set; } = null!;

    public bool IsActive { get; set; } = true;
}