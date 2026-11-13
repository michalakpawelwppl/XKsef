using Microsoft.AspNetCore.Authorization;

namespace xKsef.Api.Authorization;

public static class ApplicationOperatorAuthorization
{
    public const string PolicyName = "ApplicationOperator";

    public static IServiceCollection AddApplicationOperatorAuthorization(this IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            options.AddPolicy(PolicyName, policy =>
            {
                policy.RequireAuthenticatedUser();
                policy.AddRequirements(new ApplicationOperatorRequirement());
            });
        });
        services.AddScoped<IAuthorizationHandler, ApplicationOperatorAuthorizationHandler>();
        return services;
    }
}

internal sealed class ApplicationOperatorRequirement : IAuthorizationRequirement
{
}
