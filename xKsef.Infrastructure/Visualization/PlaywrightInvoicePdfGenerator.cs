using Microsoft.Playwright;
using xKsef.Application.Invoices.Visualization;

namespace xKsef.Infrastructure.Invoices.Visualization;

public sealed class PlaywrightInvoicePdfGenerator
    : IInvoiceVisualizationService
{
    private readonly IInvoiceHtmlTransformer _htmlTransformer;

    public PlaywrightInvoicePdfGenerator(
        IInvoiceHtmlTransformer htmlTransformer)
    {
        _htmlTransformer = htmlTransformer;
    }

    public async Task<byte[]> GeneratePdfAsync(
        string xml,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        // 1. XML -> HTML
        string html = _htmlTransformer.Transform(xml);

        // 2. Uruchomienie Playwright
        using var playwright = await Playwright.CreateAsync();

        await using var browser = await playwright.Chromium.LaunchAsync(
            new BrowserTypeLaunchOptions
            {
                Headless = true
            });

        // 3. Utworzenie nowej strony
        var page = await browser.NewPageAsync();

        // 4. Wczytanie wygenerowanego HTML
        await page.SetContentAsync(html);

        // 5. Ustawienie trybu drukowania
        await page.EmulateMediaAsync(
            new PageEmulateMediaOptions
            {
                Media = Media.Print
            });

        // 6. Generowanie PDF
        byte[] pdf = await page.PdfAsync(
            new PagePdfOptions
            {
                Format = "A4",
                PrintBackground = true,
                PreferCSSPageSize = true
            });

        // 7. Zwrócenie gotowego dokumentu
        return pdf;
    }

    public Task<byte[]> GenerateOfficialPdfAsync(
    string xml,
    string ksefNumber,
    CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException();
    }
}