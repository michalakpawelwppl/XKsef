using xKsef.Contracts.Tenants;

namespace xKsef.Wpf.Services;

public sealed class TenantContext
{
    public TenantDto? CurrentTenant { get; private set; }
    public Guid? TenantId => CurrentTenant?.Id;
    public bool HasTenant => CurrentTenant is not null;
    public event EventHandler? TenantChanged;
    public void SetTenant(TenantDto? tenant)
    {
        if (CurrentTenant?.Id == tenant?.Id) return;
        CurrentTenant = tenant;
        TenantChanged?.Invoke(this, EventArgs.Empty);
    }
}