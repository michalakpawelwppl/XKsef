namespace xKsef.Domain.Tenants;

public sealed class Tenant
{
    public Guid Id { get; set; } = Guid.NewGuid();

    public string Name { get; set; } = string.Empty;

    public bool IsActive { get; set; } = true;
    public Guid TaxEntityId { get; set; }

    public TaxEntity TaxEntity { get; set; } = null!;
}