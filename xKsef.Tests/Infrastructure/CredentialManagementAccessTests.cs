using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using xKsef.Api.Authorization;
using xKsef.Application.Tenants;

namespace xKsef.Tests.Infrastructure;

public sealed class CredentialManagementAccessTests
{
    [Theory]
    [InlineData("User", true)]
    [InlineData("Admin", true)]
    [InlineData("User", false)]
    [InlineData("Admin", false)]
    public async Task CurrentMembershipRatherThanRoleControlsContextualCredentials(string role, bool member)
    {
        var tenant = Guid.NewGuid();
        var http = new DefaultHttpContext { User = new ClaimsPrincipal(new ClaimsIdentity(new[] { new Claim(ClaimTypes.NameIdentifier, "owner"), new Claim(ClaimTypes.Role, role) }, "jwt")) };
        http.Request.Headers["X-Tenant-Id"] = tenant.ToString();
        var context = new AuthorizationFilterContext(new ActionContext(http, new RouteData(), new ActionDescriptor()), []);
        await new CredentialManagementAuthorizationFilter(new Access(member, tenant)).OnAuthorizationAsync(context);
        if (member) { Assert.Null(context.Result); Assert.Equal(tenant, http.Items["TenantId"]); }
        else { Assert.IsType<ForbidResult>(context.Result); Assert.Null(http.Items["TenantId"]); }
    }
    private sealed class Access(bool allowed, Guid tenant) : ITenantAccessService
    {
        public Task<bool> HasAccessAsync(string userId, Guid tenantId, CancellationToken cancellationToken = default)
        { Assert.Equal("owner", userId); Assert.Equal(tenant, tenantId); return Task.FromResult(allowed); }
        public Task<IReadOnlyList<string>> GetRolesAsync(string userId, Guid tenantId, CancellationToken cancellationToken = default) => throw new InvalidOperationException("Global or tenant roles must not gate personal credentials.");
    }
}