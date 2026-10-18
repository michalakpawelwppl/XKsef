using Xunit;
using xKsef.Infrastructure.Invoices.Visualization;

namespace xKsef.Tests;

public class KsefInvoiceXsltTransformerTests
{
    [Fact]
    public void Transform_ValidInvoiceXml_ReturnsHtml()
    {
        // Arrange
        string xml = """
            <?xml version="1.0" encoding="utf-8"?>
            <Faktura xmlns="http://crd.gov.pl/wzor/2025/06/25/13775/">
                <Naglowek>
                    <KodFormularza kodSystemowy="FA (3)"
                                   wersjaSchemy="1-0E">FA</KodFormularza>
                    <WariantFormularza>3</WariantFormularza>
                    <DataWytworzeniaFa>2026-10-18T10:00:00Z</DataWytworzeniaFa>
                    <SystemInfo>xKsef</SystemInfo>
                </Naglowek>
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

        // Act
        string html = transformer.Transform(xml);

        // Assert
        Assert.Contains("FV/1/2026", html);
        Assert.Contains("Sprzedawca Testowy", html);
        Assert.Contains("Nabywca Testowy", html);
    }
}