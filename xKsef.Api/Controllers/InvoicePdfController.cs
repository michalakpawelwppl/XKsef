using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xKsef.Api.Authorization;
using xKsef.Application.Invoices;
using xKsef.Application.Invoices.Pdf;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize]
[ServiceFilter(typeof(TenantAuthorizationFilter))]
[Route("api/invoices")]
public sealed class InvoicePdfController : ControllerBase
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IInvoicePdfGenerator _pdfGenerator;
    private readonly IInvoiceService _invoiceService;

    public InvoicePdfController(IInvoiceRepository invoiceRepository, IInvoicePdfGenerator pdfGenerator, IInvoiceService invoiceService)
    {
        _invoiceRepository = invoiceRepository;
        _pdfGenerator = pdfGenerator;
        _invoiceService = invoiceService;
    }

    [HttpGet("{ksefNumber}/pdf")]
    public async Task<IActionResult> GetPdfAsync(string ksefNumber, CancellationToken cancellationToken)
    {
        if (HttpContext.Items["TenantId"] is not Guid tenantId)
            return Forbid();

        var invoice = await _invoiceRepository.GetByKsefNumberAsync(tenantId, ksefNumber, cancellationToken);

        if (invoice is null)
            return NotFound();

        byte[] pdf = await _pdfGenerator.GenerateAsync(invoice.XmlBytes, ksefNumber, cancellationToken);

        return File(pdf, "application/pdf", "invoice.pdf");
    }
}
