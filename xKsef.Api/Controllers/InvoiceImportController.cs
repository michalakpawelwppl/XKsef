using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xKsef.Api.Authorization;
using xKsef.Application.Invoices;
using xKsef.Contracts.Invoices;
using InvoiceDirection = xKsef.Contracts.Invoices.InvoiceDirection;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize]
[ServiceFilter(typeof(TenantAuthorizationFilter))]
[Route("api/invoices/import")]
public sealed class InvoiceImportController : ControllerBase
{
    private readonly string _importDirectoryPath;
    private readonly IInvoicePackageImportService _importService;
    private readonly IInvoiceService _invoiceService;

    public InvoiceImportController(IInvoicePackageImportService importService, IInvoiceService invoiceService, IConfiguration configuration)
    {
        _importService = importService;
        _invoiceService = invoiceService;

        _importDirectoryPath = configuration["InvoiceImport:DirectoryPath"] ?? string.Empty;
    }

    [HttpPost]
    public async Task<ActionResult<InvoiceImportResult>> ImportAsync([FromQuery] InvoiceDirection direction, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId)
            return Forbid();

        if (string.IsNullOrWhiteSpace(_importDirectoryPath))
            return BadRequest("Nie skonfigurowano katalogu importu faktur.");

        var result = await _importService.ImportFromDirectoryAsync(tenantId, _importDirectoryPath, (Domain.Invoices.InvoiceDirection)direction, cancellationToken);

        return Ok(result);
    }

    [HttpPost("ksef")]
    public async Task<ActionResult<InvoicePackageReadResult>> ImportFromKsefAsync([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] InvoiceDirection direction, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId)
            return Forbid();
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();
        if (from > to)
            return BadRequest("Data początkowa nie może być późniejsza niż data końcowa.");

        if (direction is not InvoiceDirection.Sales and not InvoiceDirection.Purchase)
            return BadRequest("Nieprawidłowy kierunek faktur. Dozwolone wartości: Sales, Purchase.");

        var result = await _invoiceService.ImportInvoicesAsync(userId, tenantId, from, to, (Domain.Invoices.InvoiceDirection)direction, cancellationToken);

        return Ok(result);
    }
}