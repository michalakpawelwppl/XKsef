namespace xKsef.Infrastructure.Ksef.Authentication;

public sealed class KsefTokenStore
{
    private readonly object _sync = new();
    private readonly Dictionary<(string UserId, Guid TenantId), KsefTokens> _tokens = new();

    public void Set(string userId, Guid tenantId, KsefTokens tokens)
    {
        lock (_sync)
        {
            _tokens[(userId, tenantId)] = tokens;
        }
    }

    public KsefTokens? Get(string userId, Guid tenantId)
    {
        lock (_sync)
        {
            return _tokens.TryGetValue((userId, tenantId), out var tokens) ? tokens : null;
        }
    }

    public void Clear(string userId, Guid tenantId)
    {
        lock (_sync)
        {
            _tokens.Remove((userId, tenantId));
        }
    }
}