using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using xKsef.Application.Invoices;

namespace xKsef.Api.Controllers;



[ApiController]
[Authorize]
[Route("api/invoices/import")]
public sealed class InvoiceImportController : ControllerBase
{
    private readonly string _importDirectoryPath;
    private readonly IInvoicePackageImportService _importService;

    public InvoiceImportController(
        IInvoicePackageImportService importService,
        IConfiguration configuration)
    {
        _importService = importService;

        _importDirectoryPath = configuration[
            "InvoiceImport:DirectoryPath"]
            ?? throw new InvalidOperationException(
                "Nie skonfigurowano katalogu importu faktur.");
    }

    [HttpPost]
    public async Task<ActionResult<InvoiceImportResult>> ImportAsync(
    CancellationToken cancellationToken)
    {
        var result = await _importService.ImportFromDirectoryAsync(
            _importDirectoryPath,
            cancellationToken);

        return Ok(result);
    }
}