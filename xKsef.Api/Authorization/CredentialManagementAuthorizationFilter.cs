using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using xKsef.Application.Tenants;

namespace xKsef.Api.Authorization;

public sealed class CredentialManagementAuthorizationFilter : IAsyncAuthorizationFilter
{
    private readonly ITenantAccessService _tenantAccessService;

    public CredentialManagementAuthorizationFilter(ITenantAccessService tenantAccessService)
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

        string? userId = context.HttpContext.User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (string.IsNullOrWhiteSpace(userId))
        {
            context.Result = new UnauthorizedResult();
            return;
        }

        string tenantHeader = context.HttpContext.Request.Headers["X-Tenant-Id"].ToString();

        if (!Guid.TryParse(tenantHeader, out Guid tenantId) || tenantId == Guid.Empty)
        {
            context.Result = new BadRequestObjectResult("Brak lub nieprawidłowy nagłówek X-Tenant-Id.");
            return;
        }

        IReadOnlyList<string> roles = await _tenantAccessService.GetRolesAsync(userId, tenantId, context.HttpContext.RequestAborted);

        if (!roles.Contains("Admin", StringComparer.OrdinalIgnoreCase))
        {
            context.Result = new ForbidResult();
            return;
        }

        context.HttpContext.Items["TenantId"] = tenantId;
    }
}