using Microsoft.AspNetCore.Mvc;
using xKsef.Application.Exceptions;
using xKsef.Contracts.Invoices;
using xKsef.Domain.Invoices;
namespace xKsef.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InvoicesController : ControllerBase
{
    private readonly IInvoiceService _invoiceService;

    public InvoicesController(IInvoiceService invoiceService)
    {
        _invoiceService = invoiceService;
    }

    [HttpGet]
    public async Task<IActionResult> GetInvoices([FromQuery] DateOnly from, [FromQuery] DateOnly to, [FromQuery] InvoiceDirection direction, CancellationToken cancellationToken)
    {
        if (from > to)
            return BadRequest("Niepoprawny zakres dat.");

        if (!Enum.IsDefined(direction))
            return BadRequest("Niepoprawny kierunek faktur.");

        try
        {
            var invoices = await _invoiceService.GetInvoicesAsync(from, to, direction, cancellationToken);

            var result = invoices.Select(invoice => new InvoiceDto
            {
                KsefNumber = invoice.KsefNumber,
                InvoiceNumber = invoice.InvoiceNumber,
                SellerName = invoice.SellerName,
                SellerNip = invoice.SellerNip,
                IssueDate = invoice.IssueDate,
                GrossAmount = invoice.GrossAmount,
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

    [HttpGet("{ksefNumber}/xml")]
    public async Task<IActionResult> GetInvoiceXml(string ksefNumber, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(ksefNumber))
            return BadRequest("Numer KSeF nie może być pusty.");

        try
        {
            var xml = await _invoiceService.GetInvoiceXmlAsync( ksefNumber, cancellationToken);

            return Content(xml, "application/xml; charset=utf-8");
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


}