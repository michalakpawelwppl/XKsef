using System.Collections.Concurrent;

namespace xKsef.CredentialService.Services;

public sealed class KsefAuthenticationLock
{
    private readonly ConcurrentDictionary<(Guid TenantId, string UserId), SemaphoreSlim> _locks = new();

    public SemaphoreSlim GetLock(Guid tenantId, string userId)
    {
        return _locks.GetOrAdd((tenantId, userId), _ => new SemaphoreSlim(1, 1));
    }
}