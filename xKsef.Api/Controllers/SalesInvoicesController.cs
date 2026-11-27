using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xKsef.Api.Authorization;
using xKsef.Application.SalesInvoices;
using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.SalesInvoices;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize]
[ServiceFilter(typeof(TenantAuthorizationFilter))]
[Route("api/sales-invoices")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
[RequestSizeLimit(4_000_000)]
public sealed class SalesInvoicesController(ISalesInvoiceService service) : ControllerBase
{
    private Guid TenantId => (Guid)HttpContext.Items["TenantId"]!;
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] SalesInvoiceDraftDto draft, CancellationToken cancellationToken)
    {
        var invoice = await service.CreateAsync(TenantId, UserId, SalesInvoiceContractMapper.ToDomain(draft), cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = invoice.Id }, SalesInvoiceContractMapper.ToDto(invoice));
    }
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken)
    {
        var invoice = await service.GetAsync(TenantId, id, cancellationToken);
        return invoice is null ? NotFound() : Ok(SalesInvoiceContractMapper.ToDto(invoice));
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await service.UpdateAsync(TenantId, id, UserId, Version(request.Version), SalesInvoiceContractMapper.ToDomain(request.Draft), cancellationToken);
        return Ok(SalesInvoiceContractMapper.ToDto(invoice));
    }
    [HttpPost("{id:guid}/recalculate")]
    public async Task<IActionResult> Recalculate(Guid id, [FromBody] RecalculateSalesInvoiceRequest request, CancellationToken cancellationToken)
    {
        var invoice = await service.RecalculateAsync(TenantId, id, UserId, Version(request.Version), cancellationToken);
        return Ok(SalesInvoiceContractMapper.ToDto(invoice));
    }
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken, [FromQuery] int page = 1, [FromQuery] int pageSize = 50) => Ok(SalesInvoiceContractMapper.ToDto(await service.ListDraftsAsync(TenantId, page, pageSize, cancellationToken)));

    private static byte[] Version(string? value)
    {
        if (value is not null)
        {
            Span<byte> buffer = stackalloc byte[8];
            if (Convert.TryFromBase64String(value, buffer, out var count) && count == 8) return buffer.ToArray();
        }
        throw new SalesInvoiceValidationException([new("version", "InvalidVersion", "Wymagana wersja Base64 zawierająca 8 bajtów.")]);
    }
}
