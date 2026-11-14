using Microsoft.AspNetCore.Mvc;

namespace xKsef.CredentialService.Middleware;

public sealed class KsefEnvironmentMiddleware
{
    private readonly RequestDelegate _next;
    private readonly string _environment;

    public KsefEnvironmentMiddleware(RequestDelegate next, string environment)
    {
        _next = next;
        _environment = environment;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        if (!context.Request.Path.StartsWithSegments("/internal/ksef-credentials"))
        {
            await _next(context);
            return;
        }

        var values = context.Request.Headers["X-KSeF-Environment"];
        string? expected = values.Count == 1 ? values[0] : null;
        bool valid = expected is "TEST" or "PROD";
        if (valid && expected == _environment)
        {
            await _next(context);
            return;
        }

        var problem = new ProblemDetails
        {
            Status = valid ? StatusCodes.Status409Conflict : StatusCodes.Status400BadRequest,
            Title = "Kontrola środowiska KSeF",
            Detail = valid ? "Niezgodne środowisko KSeF. Operacja została zablokowana." : "Wymagany jest pojedynczy nagłówek X-KSeF-Environment o wartości TEST lub PROD."
        };
        problem.Extensions["code"] = valid ? "ksef_environment_mismatch" : "ksef_environment_required";
        problem.Extensions["ksefEnvironment"] = _environment;
        context.Response.StatusCode = problem.Status.Value;
        await context.Response.WriteAsJsonAsync(problem, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: context.RequestAborted);
    }
}
