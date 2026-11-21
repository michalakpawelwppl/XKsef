namespace xKsef.Domain.Contractors;

public sealed class Contractor
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; }
}