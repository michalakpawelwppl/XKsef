using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using xKsef.Application.Exceptions;

namespace xKsef.Api.ExceptionHandlers;

public sealed class CredentialServiceExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
    {
        if (exception is not CredentialServiceException failure) return false;

        int status = failure.Failure == CredentialServiceFailure.InvalidHealthResponse
            ? StatusCodes.Status502BadGateway : StatusCodes.Status503ServiceUnavailable;
        httpContext.Response.StatusCode = status;
        await httpContext.Response.WriteAsJsonAsync(new ProblemDetails
        {
            Status = status,
            Title = "Błąd kontroli środowiska CredentialService",
            Detail = failure.Message
        }, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
