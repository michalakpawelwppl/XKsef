using System.Security.Claims;

namespace xKsef.CredentialService.Endpoints;

public static class CredentialServiceHealthEndpoint
{
    public static RouteHandlerBuilder MapCredentialServiceHealth(this IEndpointRouteBuilder endpoints, string environment)
    {
        return endpoints.MapGet("/internal/health", (ClaimsPrincipal user) =>
            Results.Ok(new { Success = true, Client = user.Identity?.Name, KsefEnvironment = environment })).RequireAuthorization();
    }
}
