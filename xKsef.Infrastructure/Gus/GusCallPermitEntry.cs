using xKsef.Application.Gus;

namespace xKsef.Infrastructure.Gus;

/// <summary>Technical admission history; independent of tenants and contractor data.</summary>
public sealed class GusCallPermitEntry
{
    public long Id { get; set; }
    public GusRateLimitEnvironment Environment { get; set; }
    public DateTime GrantedAtUtc { get; set; }
}
