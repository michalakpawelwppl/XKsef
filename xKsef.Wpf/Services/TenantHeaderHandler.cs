using System.Net.Http;
using xKsef.Wpf.Http;

namespace xKsef.Wpf.Services;

public sealed class TenantHeaderHandler : DelegatingHandler
{
    private readonly TenantContext _tenantContext;

    public TenantHeaderHandler(TenantContext tenantContext)
    {
        _tenantContext = tenantContext;
    }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.TryGetApiRequestContext(out ApiRequestContext requestContext))
        {
            switch (requestContext.Scope)
            {
                case ApiRequestScope.Anonymous:
                case ApiRequestScope.User:
                    request.Headers.Remove("X-Tenant-Id");
                    break;
                case ApiRequestScope.TenantHeader:
                    if (requestContext.TenantId is not Guid contextTenantId)
                        throw new InvalidOperationException("The request context requires a tenant ID.");

                    request.Headers.Remove("X-Tenant-Id");
                    request.Headers.TryAddWithoutValidation("X-Tenant-Id", contextTenantId.ToString("D"));
                    break;
                case ApiRequestScope.TenantRoute:
                    if (requestContext.TenantId is null)
                        throw new InvalidOperationException("The request context requires a tenant ID.");

                    request.Headers.Remove("X-Tenant-Id");
                    break;
                default:
                    throw new InvalidOperationException("The request context has an unsupported scope.");
            }

            return base.SendAsync(request, cancellationToken);
        }

        if (_tenantContext.TenantId is Guid tenantId)
            request.Headers.TryAddWithoutValidation("X-Tenant-Id", tenantId.ToString("D"));

        return base.SendAsync(request, cancellationToken);
    }
}