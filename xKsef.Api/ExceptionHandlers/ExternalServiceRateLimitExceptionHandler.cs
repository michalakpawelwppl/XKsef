using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using xKsef.Application.Exceptions;

namespace xKsef.Api.ExceptionHandlers;

public sealed class ExternalServiceRateLimitExceptionHandler
    : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not ExternalServiceRateLimitException rateLimitException)
        {
            return false;
        }

        httpContext.Response.StatusCode =
            StatusCodes.Status429TooManyRequests;

        if (rateLimitException.RetryAfter is { } retryAfter)
        {
            var seconds = Math.Max(
                0,
                (int)Math.Ceiling(retryAfter.TotalSeconds));

            httpContext.Response.Headers.RetryAfter =
                seconds.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status429TooManyRequests,
            Title = "Przekroczono limit zapytań do KSeF",
            Detail = rateLimitException.Message
        };

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            cancellationToken);

        return true;
    }
}