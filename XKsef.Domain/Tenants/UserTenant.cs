namespace xKsef.Domain.Tenants;

public sealed class UserTenant
{
    public Guid TenantId { get; set; }

    public string UserId { get; set; } = string.Empty;

    public string Role { get; set; } = "User";

    public Guid? PreferredKsefCredentialId { get; set; }
}