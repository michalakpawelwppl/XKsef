using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using System.Security.Claims;
using xKsef.Infrastructure.Identity;

namespace xKsef.Api.Authorization;

internal sealed class ApplicationOperatorAuthorizationHandler : AuthorizationHandler<ApplicationOperatorRequirement>
{
    private readonly IConfiguration _configuration;
    private readonly UserManager<ApplicationUser> _userManager;

    public ApplicationOperatorAuthorizationHandler(IConfiguration configuration, UserManager<ApplicationUser> userManager)
    {
        _configuration = configuration;
        _userManager = userManager;
    }

    protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, ApplicationOperatorRequirement requirement)
    {
        if (context.User.Identity?.IsAuthenticated != true) return;

        string? operatorId = _configuration["ApplicationOperator:UserId"];
        string? userId = context.User.FindFirstValue(ClaimTypes.NameIdentifier);

        // Identity uses string keys; do not interpret the configured ID as a login, role or GUID.
        if (string.IsNullOrWhiteSpace(operatorId) || operatorId.Length > 450 || operatorId != operatorId.Trim() || operatorId.Any(char.IsControl))
            return;
        if (!string.Equals(operatorId, userId, StringComparison.Ordinal))
            return;

        if (await _userManager.FindByIdAsync(operatorId) is not null)
            context.Succeed(requirement);
    }
}
