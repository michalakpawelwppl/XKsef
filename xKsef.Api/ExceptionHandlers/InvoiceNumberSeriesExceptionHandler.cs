using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using xKsef.Application.Tenants;
using xKsef.Domain.InvoiceNumberSeries;

namespace xKsef.Api.ExceptionHandlers;

public sealed class InvoiceNumberSeriesExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        if (!context.Request.Path.StartsWithSegments("/api/invoice-number-series")) return false;
        ProblemDetails problem;
        if (exception is InvoiceNumberSeriesValidationException invalid)
        {
            problem = new ValidationProblemDetails(invalid.Issues.GroupBy(x => x.Path).ToDictionary(x => x.Key, x => x.Select(i => i.Message).ToArray())) { Status = 400, Title = invalid.Message };
            problem.Extensions["issues"] = invalid.Issues;
        }
        else if (exception is InvoiceNumberSeriesConflictException conflict) { problem = new() { Status = 409, Title = conflict.Message }; problem.Extensions["code"] = conflict.Code; }
        else if (exception is TenantPermissionDeniedException denied) { problem = new() { Status = 403, Title = denied.Message }; problem.Extensions["permission"] = denied.Permission; }
        else if (exception is KeyNotFoundException) problem = new() { Status = 404, Title = "Nie znaleziono serii w tenantcie." };
        else return false;
        context.Response.StatusCode = problem.Status!.Value;
        await context.Response.WriteAsJsonAsync(problem, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
