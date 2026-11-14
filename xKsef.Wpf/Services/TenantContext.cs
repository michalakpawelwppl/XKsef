using xKsef.Contracts.Tenants;

namespace xKsef.Wpf.Services;

public sealed class TenantContext
{
    public TenantDto? CurrentTenant { get; private set; }
    public long Version { get; private set; }
    public Guid? TenantId => CurrentTenant?.Id;
    public bool HasTenant => CurrentTenant is not null;
    public event EventHandler? TenantChanged;
    public void SetTenant(TenantDto? tenant)
    {
        if (CurrentTenant == tenant) return;
        CurrentTenant = tenant;
        Version++;
        TenantChanged?.Invoke(this, EventArgs.Empty);
    }
}