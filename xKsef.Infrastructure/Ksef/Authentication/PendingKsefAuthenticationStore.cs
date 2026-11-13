using Microsoft.Extensions.Caching.Memory;

namespace xKsef.Infrastructure.Ksef.Authentication;

public sealed class PendingKsefAuthenticationStore
{
    private readonly IMemoryCache _cache;
    private readonly object _sync = new();

    public PendingKsefAuthenticationStore(IMemoryCache cache)
    {
        _cache = cache;
    }

    public void Save(PendingKsefAuthentication operation)
    {
        _cache.Set(operation.Id, operation, TimeSpan.FromMinutes(5));
    }

    public PendingKsefAuthentication? Get(Guid operationId)
    {
        return _cache.TryGetValue(operationId, out PendingKsefAuthentication? operation) ? operation : null;
    }

    public void Remove(Guid operationId)
    {
        _cache.Remove(operationId);
    }
    public PendingKsefAuthentication? Take(Guid operationId)
    {
        lock (_sync)
        {
            if (!_cache.TryGetValue(operationId, out PendingKsefAuthentication? operation))
                return null;

            _cache.Remove(operationId);
            return operation;
        }
    }

    public PendingKsefAuthentication? Take(Guid operationId, string userId, Guid tenantId)
    {
        lock (_sync)
        {
            if (!_cache.TryGetValue(operationId, out PendingKsefAuthentication? operation))
                return null;

            if (operation!.UserId != userId || operation.TenantId != tenantId)
                return null;

            _cache.Remove(operationId);
            return operation;
        }
    }
}