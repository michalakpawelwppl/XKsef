using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xKsef.Api.Authorization;
using xKsef.Infrastructure.Ksef.Invoices;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize]
[ServiceFilter(typeof(TenantAuthorizationFilter))]
[Route("api/ksef-test")]
public class KsefTestController : ControllerBase
{
    private readonly KsefInvoiceService _invoiceService;

    public KsefTestController(KsefInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    [HttpGet("invoice-count")]
    public async Task<IActionResult> GetInvoiceCount(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        CancellationToken cancellationToken)
    {
        if (from > to)
        {
            return BadRequest(
                "Data początkowa nie może być późniejsza od końcowej.");
        }
        if (HttpContext.Items["TenantId"] is not Guid tenantId)
            return Forbid();

        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;

        if (string.IsNullOrWhiteSpace(userId))
            return Unauthorized();
        var count = await _invoiceService.GetInvoiceCountAsync(userId, tenantId, from, to, cancellationToken);

        return Ok(new
        {
            Count = count,
            From = from,
            To = to
        });
    }
}