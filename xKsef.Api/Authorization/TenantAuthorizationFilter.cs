using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using xKsef.Application.Tenants;

namespace xKsef.Api.Authorization;

public sealed class TenantAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly ITenantAccessService _tenantAccessService;

    public TenantAuthorizationFilter(ITenantAccessService tenantAccessService)
    {
        _tenantAccessService = tenantAccessService;
    }

    public async Task OnAuthorizationAsync(AuthorizationFilterContext context)
    {
        if (context.HttpContext.User.Identity?.IsAuthenticated != true)
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var userId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        var tenantHeader = context.HttpContext.Request.Headers["X-Tenant-Id"].ToString();

        if (!Guid.TryParse(tenantHeader, out var tenantId) || tenantId == Guid.Empty)
        {
            context.Result = new BadRequestObjectResult("Brak lub nieprawidłowy nagłówek X-Tenant-Id.");
            return;
        }

        var hasAccess = await _tenantAccessService.HasAccessAsync(userId, tenantId, context.HttpContext.RequestAborted);

        if (!hasAccess)
        {
            context.Result = new ForbidResult();
            return;
        }

        context.HttpContext.Items["TenantId"] = tenantId;
    }
}