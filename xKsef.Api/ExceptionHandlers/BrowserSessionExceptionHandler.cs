using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using xKsef.Api.Authentication;
using xKsef.Infrastructure.Authentication;

namespace xKsef.Api.ExceptionHandlers;

public sealed class BrowserSessionExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        int status = exception switch { BrowserSessionStoreUnavailableException => 503, BrowserSessionIdentityChangedException => 401, _ => 0 };
        if (status == 0) return false;
        context.Response.StatusCode = status;
        context.Response.Headers.CacheControl = "no-store";
        await context.Response.WriteAsJsonAsync(new ProblemDetails { Status = status,
            Title = status == 503 ? "Magazyn sesji jest chwilowo niedostępny. Spróbuj ponownie później." : "Stan konta uległ zmianie. Zaloguj się ponownie." },
            options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
