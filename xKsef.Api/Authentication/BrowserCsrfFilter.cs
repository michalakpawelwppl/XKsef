using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using xKsef.Infrastructure.Authentication;

namespace xKsef.Api.Authentication;

[AttributeUsage(AttributeTargets.Class)]
public sealed class BrowserRequestAttribute : Attribute { }

// Resource filters run after authorization, but before controllers and any business writes.
public sealed class BrowserCsrfFilter(IAntiforgery antiforgery) : IAsyncResourceFilter
{
    public async Task OnResourceExecutionAsync(ResourceExecutingContext context, ResourceExecutionDelegate next)
    {
        var http = context.HttpContext;
        bool browserEndpoint = http.GetEndpoint()?.Metadata.GetMetadata<BrowserRequestAttribute>() is not null;
        if (browserEndpoint && http.Request.Headers.ContainsKey("Authorization"))
        {
            context.Result = new UnauthorizedResult(); return;
        }
        bool cookie = http.User.Identity?.IsAuthenticated == true && http.User.Identity.AuthenticationType == BrowserSessionOptions.AuthenticationScheme;
        if (browserEndpoint || cookie) http.Response.Headers.CacheControl = "no-store";
        bool safe = HttpMethods.IsGet(http.Request.Method) || HttpMethods.IsHead(http.Request.Method) || HttpMethods.IsOptions(http.Request.Method);
        if (!safe)
        {
            bool bearer = http.Request.Headers.ContainsKey("Authorization") && (await http.AuthenticateAsync(JwtBearerDefaults.AuthenticationScheme)).Succeeded;
            if (http.Request.Headers.ContainsKey("Authorization") && !bearer) { context.Result = new UnauthorizedResult(); return; }
            if (!bearer && (browserEndpoint || cookie))
            {
                try { await antiforgery.ValidateRequestAsync(http); }
                catch (AntiforgeryValidationException)
                {
                    context.Result = new ObjectResult(new ProblemDetails { Status = 400, Title = "Nieprawidłowy token CSRF. Pobierz nowy token i ponów żądanie." })
                    { StatusCode = 400, ContentTypes = { "application/problem+json" } };
                    return;
                }
            }
        }
        await next();
    }
}
