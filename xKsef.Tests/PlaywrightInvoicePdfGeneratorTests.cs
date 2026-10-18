using Xunit;
using xKsef.Infrastructure.Invoices.Visualization;

namespace xKsef.Tests;

public class PlaywrightInvoicePdfGeneratorTests
{
    [Fact]
    public async Task GeneratePdfAsync_ValidXml_ReturnsPdf()
    {
        // Arrange
        string xml = """
            <Faktura xmlns="http://crd.gov.pl/wzor/2025/06/25/13775/">
                <Podmiot1>
                    <DaneIdentyfikacyjne>
                        <NIP>1234567890</NIP>
                        <Nazwa>Sprzedawca Testowy</Nazwa>
                    </DaneIdentyfikacyjne>
                </Podmiot1>
                <Podmiot2>
                    <DaneIdentyfikacyjne>
                        <NIP>9876543210</NIP>
                        <Nazwa>Nabywca Testowy</Nazwa>
                    </DaneIdentyfikacyjne>
                </Podmiot2>
                <Fa>
                    <KodWaluty>PLN</KodWaluty>
                    <P_1>2026-10-18</P_1>
                    <P_2>FV/1/2026</P_2>
                    <P_15>123.00</P_15>
                    <RodzajFaktury>VAT</RodzajFaktury>
                </Fa>
            </Faktura>
            """;

        var transformer = new KsefInvoiceXsltTransformer();
        var generator = new PlaywrightInvoicePdfGenerator(transformer);

        // Act
        byte[] pdf = await generator.GeneratePdfAsync(xml);
        string pdfPath = Path.Combine(
    Path.GetTempPath(),
    "xKsef-test-invoice.pdf");

        await File.WriteAllBytesAsync(pdfPath, pdf);

        Console.WriteLine($"PDF zapisany: {pdfPath}");
        // Assert
        Assert.NotEmpty(pdf);
        Assert.Equal("%PDF-", System.Text.Encoding.ASCII.GetString(pdf, 0, 5));
    }
}