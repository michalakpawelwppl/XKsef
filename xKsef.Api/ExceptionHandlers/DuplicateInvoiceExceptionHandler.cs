using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using xKsef.Application.Exceptions;

namespace xKsef.Api.ExceptionHandlers;

public sealed class DuplicateInvoiceExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        if (exception is not DuplicateInvoiceException duplicateException)
        {
            return false;
        }

        httpContext.Response.StatusCode =
            StatusCodes.Status409Conflict;

        var problem = new ProblemDetails
        {
            Status = StatusCodes.Status409Conflict,
            Title = "Faktura już istnieje",
            Detail = $"Faktura o numerze KSeF '{duplicateException.KsefNumber}' już istnieje."
        };

        await httpContext.Response.WriteAsJsonAsync(
            problem,
            cancellationToken);

        return true;
    }
}