using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using xKsef.Domain.SalesInvoices;
using xKsef.Application.Tenants;
using xKsef.Application.SalesInvoices.Snapshots;

namespace xKsef.Api.ExceptionHandlers;

public sealed class SalesInvoiceExceptionHandler : IExceptionHandler
{
    public async ValueTask<bool> TryHandleAsync(HttpContext context, Exception exception, CancellationToken cancellationToken)
    {
        // Restrict generic not-found handling to this module's endpoint.
        if (!context.Request.Path.StartsWithSegments("/api/sales-invoices")) return false;
        ProblemDetails problem;
        if (exception is SalesInvoiceValidationException validation)
        {
            problem = new ValidationProblemDetails(validation.Issues.GroupBy(x => x.Path).ToDictionary(x => x.Key, x => x.Select(i => i.Message).ToArray())) { Status = 400, Title = validation.Message };
            problem.Extensions["issues"] = validation.Issues;
        }
        else if (exception is TenantPermissionDeniedException denied)
        {
            problem = new() { Status = 403, Title = denied.Message };
            problem.Extensions["permission"] = denied.Permission;
        }
        else if (exception is SalesInvoiceApprovalSnapshotException snapshot)
        {
            problem = new() { Status = 409, Title = snapshot.Message };
            problem.Extensions["code"] = snapshot.Code;
        }
        else if (exception is SalesInvoiceConflictException) problem = new() { Status = 409, Title = exception.Message };
        else if (exception is KeyNotFoundException) problem = new() { Status = 404, Title = "Nie znaleziono faktury w tenantcie." };
        else return false;
        context.Response.StatusCode = problem.Status!.Value;
        await context.Response.WriteAsJsonAsync(problem, options: (System.Text.Json.JsonSerializerOptions?)null, contentType: "application/problem+json", cancellationToken: cancellationToken);
        return true;
    }
}
