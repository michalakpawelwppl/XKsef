namespace xKsef.Application.Tenants;

public sealed record CreateTenantResult(Guid? TenantId, string? Error);
