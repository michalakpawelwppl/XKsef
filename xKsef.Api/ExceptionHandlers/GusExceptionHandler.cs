using System.Globalization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using xKsef.Application.Gus;

namespace xKsef.Api.ExceptionHandlers;

public sealed class GusExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        var (status, title) = exception switch
        {
            GusRateLimitExceededException => (StatusCodes.Status429TooManyRequests, "Wyczerpano budżet oczekiwania na limit GUS"),
            GusRateLimiterUnavailableException => (StatusCodes.Status503ServiceUnavailable, "Centralny mechanizm limitowania GUS jest niedostępny"),
            GusBirException { Error: GusBirError.Timeout } => (StatusCodes.Status504GatewayTimeout, "Przekroczono czas komunikacji z GUS"),
            GusBirException => (StatusCodes.Status502BadGateway, "Błąd zewnętrznej usługi GUS"),
            _ => (0, "")
        };
        if (status == 0) return false;

        httpContext.Response.StatusCode = status;
        if (exception is GusRateLimitExceededException limited)
        {
            // Retry-After is an integral number of seconds. Unknown/already elapsed hints use one second.
            var seconds = Math.Max(1, Math.Ceiling(limited.RetryAfter?.TotalSeconds ?? 1));
            httpContext.Response.Headers.RetryAfter = seconds.ToString("0", CultureInfo.InvariantCulture);
        }
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails { Status = status, Title = title },
            options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
