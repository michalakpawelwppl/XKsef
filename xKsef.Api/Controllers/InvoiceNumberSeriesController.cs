using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xKsef.Api.Authorization;
using xKsef.Application.InvoiceNumbering;
using xKsef.Contracts.InvoiceNumberSeries;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize]
[ServiceFilter(typeof(TenantAuthorizationFilter))]
[Route("api/invoice-number-series")]
[ResponseCache(NoStore = true, Location = ResponseCacheLocation.None)]
public sealed class InvoiceNumberSeriesController(IInvoiceNumberSeriesService service) : ControllerBase
{
    private Guid TenantId => (Guid)HttpContext.Items["TenantId"]!;
    private string UserId => User.FindFirstValue(ClaimTypes.NameIdentifier)!;
    [HttpGet]
    public async Task<IActionResult> List(CancellationToken cancellationToken) => Ok(await service.ListAsync(TenantId, UserId, cancellationToken));
    [HttpGet("{id:guid}")]
    public async Task<IActionResult> Get(Guid id, CancellationToken cancellationToken) => Ok(await service.GetAsync(TenantId, id, UserId, cancellationToken));
    [HttpPost]
    public async Task<IActionResult> Create([FromBody] NumberSeriesConfigurationDto configuration, CancellationToken cancellationToken)
    {
        var series = await service.CreateAsync(TenantId, UserId, configuration, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = series.Id }, series);
    }
    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateInvoiceNumberSeriesRequest request, CancellationToken cancellationToken)
        => Ok(await service.UpdateAsync(TenantId, id, UserId, request, cancellationToken));
    [HttpPut("{id:guid}/activation")]
    public async Task<IActionResult> SetActive(Guid id, [FromBody] SetInvoiceNumberSeriesActiveRequest request, CancellationToken cancellationToken)
        => Ok(await service.SetActiveAsync(TenantId, id, UserId, request, cancellationToken));
    [HttpPut("{id:guid}/default")]
    public async Task<IActionResult> SetDefault(Guid id, [FromBody] SetInvoiceNumberSeriesDefaultRequest request, CancellationToken cancellationToken)
        => Ok(await service.SetDefaultAsync(TenantId, id, UserId, request, cancellationToken));
}
