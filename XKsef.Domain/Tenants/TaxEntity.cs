namespace xKsef.Domain.Tenants;

public sealed class TaxEntity
{
    public Guid Id { get; set; }

    public string Nip { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;
}