using Xunit;
using xKsef.Infrastructure.Invoices.Visualization;

namespace xKsef.Tests;

public class KsefInvoiceXmlParserTests
{
    [Fact]
    public void Parse_ShouldReadInvoiceNumber()
    {
        // Arrange
        string xml = """
    <Faktura xmlns="http://crd.gov.pl/wzor/2025/06/25/13775/">
        <Podmiot1>
            <DaneIdentyfikacyjne>
                <NIP>1234567890</NIP>
                <Nazwa>Firma Testowa Sp. z o.o.</Nazwa>
            </DaneIdentyfikacyjne>
        </Podmiot1>
        <Podmiot2>
        <DaneIdentyfikacyjne>
            <NIP>9876543210</NIP>
            <Nazwa>Przykładowy Nabywca Sp. z o.o.</Nazwa>
        </DaneIdentyfikacyjne>
    </Podmiot2>
    <Fa>
        <KodWaluty>EUR</KodWaluty>
        <P_1>2026-10-18</P_1>
        <P_2>FV/123/2026</P_2>
        <P_15>1230.50</P_15>
    <FaWiersz>
        <NrWierszaFa>1</NrWierszaFa>
        <P_7>Laptop Dell Latitude</P_7>
        <P_8A>szt.</P_8A>
        <P_8B>2.000</P_8B>
        <P_9A>1500.50</P_9A>
        <P_11>3001.00</P_11>
    <P_12>23</P_12>
    </FaWiersz>

    <FaWiersz>
        <NrWierszaFa>2</NrWierszaFa>
        <P_7>Monitor Samsung 27 cali</P_7>
        <P_8A>kpl.</P_8A>
        <P_8B>3.500</P_8B>
        <P_9A>850.75</P_9A>
        <P_11>2977.63</P_11>
    <P_12>8</P_12>
    </FaWiersz>
    </Fa>
    </Faktura>
    """;

        var parser = new KsefInvoiceXmlParser();

        // Act
        var result = parser.Parse(xml);

        // Assert
        Assert.Equal("FV/123/2026", result.InvoiceNumber);
        Assert.Equal(new DateOnly(2026, 10, 18), result.IssueDate);
        Assert.Equal("1234567890", result.SellerNip);
        Assert.Equal("Firma Testowa Sp. z o.o.", result.SellerName);
        Assert.Equal("9876543210", result.BuyerNip);
        Assert.Equal("Przykładowy Nabywca Sp. z o.o.", result.BuyerName);
        Assert.Equal(1230.50m, result.GrossAmount);
        Assert.Equal("EUR", result.Currency);
        Assert.Equal(2, result.Items.Count);

        Assert.Equal(1, result.Items[0].LineNumber);
        Assert.Equal(2, result.Items[1].LineNumber);
        Assert.Equal("Laptop Dell Latitude", result.Items[0].Description);
        Assert.Equal("Monitor Samsung 27 cali", result.Items[1].Description);
        Assert.Equal(2.000m, result.Items[0].Quantity);
        Assert.Equal(3.500m, result.Items[1].Quantity);
        Assert.Equal("szt.", result.Items[0].Unit);
        Assert.Equal("kpl.", result.Items[1].Unit);
        Assert.Equal(1500.50m, result.Items[0].NetUnitPrice);
        Assert.Equal(850.75m, result.Items[1].NetUnitPrice);
        Assert.Equal(3001.00m, result.Items[0].NetAmount);
        Assert.Equal(2977.63m, result.Items[1].NetAmount);
        Assert.Equal("23", result.Items[0].VatRate);
        Assert.Equal("8", result.Items[1].VatRate);
    }
}