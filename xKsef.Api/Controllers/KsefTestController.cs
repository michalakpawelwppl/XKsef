using Microsoft.AspNetCore.Mvc;
using xKsef.Infrastructure.Ksef.Invoices;

namespace xKsef.Api.Controllers;

[ApiController]
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

        var count = await _invoiceService.GetInvoiceCountAsync(
            from,
            to,
            cancellationToken);

        return Ok(new
        {
            Count = count,
            From = from,
            To = to
        });
    }
}