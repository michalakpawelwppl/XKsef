using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xKsef.Api.Authorization;
using xKsef.Application.Exceptions;
using xKsef.Application.Invoices;
using xKsef.Contracts.Invoices;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize]
[ServiceFilter(typeof(TenantAuthorizationFilter))]
[Route("api/[controller]")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _invoiceService;
    private readonly IInvoiceRepository _invoiceRepository;

    public InvoicesController(IInvoiceService invoiceService, IInvoiceRepository invoiceRepository)
    {
        _invoiceService = invoiceService;
        _invoiceRepository = invoiceRepository;
    }

    [HttpGet]
    public async Task<IActionResult> GetInvoices([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] InvoiceDirection direction, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId)
            return Forbid();
        if (from > to)
            return BadRequest("Niepoprawny zakres dat.");

        if (!Enum.IsDefined(direction))
            return BadRequest("Niepoprawny kierunek faktur.");

        try
        {
            var invoices = await _invoiceService.GetInvoicesAsync(tenantId, from, to, (xKsef.Domain.Invoices.InvoiceDirection)direction, cancellationToken);

            var result = invoices.Select(invoice => new InvoiceDto
            {
                KsefNumber = invoice.KsefNumber,
                InvoiceNumber = invoice.InvoiceNumber,
                SellerName = invoice.SellerName,
                SellerNip = invoice.SellerNip,
                IssueDate = invoice.IssueDate,
                GrossAmount = invoice.GrossAmount,
                BuyerNip = invoice.BuyerNip,
                BuyerName = invoice.BuyerName,
                NetAmount = invoice.NetAmount,
                VatAmount = invoice.VatAmount,
                VatAmountPln = invoice.VatAmountPln,
                InvoicingDate = invoice.InvoicingDate,
                Currency = invoice.Currency
            });

            return Ok(result);
        }
        catch (ExternalServiceRateLimitException ex)
        {
            if (ex.RetryAfter.HasValue)
            {
                var seconds = (int)Math.Ceiling(ex.RetryAfter.Value.TotalSeconds);

                Response.Headers.RetryAfter =Math.Max(0, seconds).ToString(System.Globalization.CultureInfo.InvariantCulture);
            }

            return StatusCode(StatusCodes.Status429TooManyRequests,new ProblemDetails
                {
                    Status = StatusCodes.Status429TooManyRequests,
                    Title = "Przekroczono limit żądań KSeF",
                    Detail = ex.Message
                });
        }
    }

    [HttpGet("query")]
    public async Task<IActionResult> Query([FromServices] IInvoiceQueryService queryService, [FromQuery, Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] DateOnly from, [FromQuery, Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] DateOnly to, [FromQuery, Microsoft.AspNetCore.Mvc.ModelBinding.BindRequired] InvoiceDirection direction, CancellationToken cancellationToken, [FromQuery] InvoiceDateKind dateKind = InvoiceDateKind.IssueDate, [FromQuery] string? timeZoneId = null)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId) return Forbid();
        InvoiceQueryFilter filter;
        try
        {
            filter = InvoiceQueryFilter.Create(from, to, direction, dateKind, timeZoneId);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(new ProblemDetails { Status = StatusCodes.Status400BadRequest, Title = "Nieprawidłowy filtr faktur", Detail = ex.Message });
        }
        return Ok(await queryService.QueryAsync(tenantId, filter, cancellationToken));
    }

    [HttpGet("{ksefNumber}/xml")]
    public async Task<IActionResult> GetInvoiceXml(string ksefNumber, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId)
            return Forbid();

        if (string.IsNullOrWhiteSpace(ksefNumber))
            return BadRequest("Numer KSeF nie może być pusty.");

        var invoice = await _invoiceRepository.GetByKsefNumberAsync(tenantId, ksefNumber, cancellationToken);

        if (invoice is null)
            return NotFound();

        return File(invoice.XmlBytes, "application/xml; charset=utf-8");
    }


}