namespace xKsef.Wpf.Http;

public enum ApiRequestScope
{
    Unspecified,
    Anonymous,
    User,
    TenantHeader,
    TenantRoute
}

public readonly record struct ApiRequestContext(
    long Generation,
    long SessionVersion,
    Guid? TenantId,
    string? AccessToken,
    CancellationToken CancellationToken,
    ApiRequestScope Scope);