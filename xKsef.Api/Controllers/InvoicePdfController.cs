using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xKsef.Application.Invoices;
using xKsef.Application.Invoices.Pdf;

namespace xKsef.Api.Controllers;

[ApiController]
[Authorize]
[Route("api/invoices")]
public sealed class InvoicePdfController : ControllerBase
{
    private readonly IInvoiceRepository _invoiceRepository;
    private readonly IInvoicePdfGenerator _pdfGenerator;
    private readonly IInvoiceService _invoiceService;

    public InvoicePdfController(
        IInvoiceRepository invoiceRepository,
        IInvoicePdfGenerator pdfGenerator,
        IInvoiceService invoiceService)
    {
        _invoiceRepository = invoiceRepository;
        _pdfGenerator = pdfGenerator;
        _invoiceService = invoiceService;
    }

    [HttpGet("{ksefNumber}/pdf")]
    public async Task<IActionResult> GetPdfAsync(
    string ksefNumber,
    CancellationToken cancellationToken)
    {
        var invoice = await _invoiceRepository.GetByKsefNumberAsync(
            ksefNumber,
            cancellationToken);

        byte[] xmlBytes;

        if (invoice is not null)
        {
            // Faktura jest w lokalnej bazie danych.
            xmlBytes = invoice.XmlBytes;
        }
        else
        {
            // Faktury nie ma w bazie — pobieramy XML z KSeF.
            string xml = await _invoiceService.GetInvoiceXmlAsync(
                ksefNumber,
                cancellationToken);

            xmlBytes = System.Text.Encoding.UTF8.GetBytes(xml);
        }

        byte[] pdf = await _pdfGenerator.GenerateAsync(
            xmlBytes,
            ksefNumber,
            cancellationToken);

        return File(
            pdf,
            "application/pdf",
            "invoice.pdf");
    }
}