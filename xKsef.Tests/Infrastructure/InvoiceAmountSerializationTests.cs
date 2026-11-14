using System.Globalization;
using System.Text.Json;
using xKsef.Contracts.Invoices;

namespace xKsef.Tests.Infrastructure;

public sealed class InvoiceAmountSerializationTests
{
    [Theory]
    [InlineData("9999999999999999.99")]
    [InlineData("0.10")]
    [InlineData("-123.45")]
    [InlineData("0.00")]
    public void SerializesExactStringsAlongsideUnchangedNumericAmounts(string text)
    {
        decimal amount = decimal.Parse(text, CultureInfo.InvariantCulture);
        var dto = new InvoiceDto { GrossAmount = amount, NetAmount = amount, VatAmount = amount, VatAmountPln = amount };
        using var document = JsonDocument.Parse(JsonSerializer.Serialize(dto, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
        foreach (string name in new[] { "grossAmount", "netAmount", "vatAmount", "vatAmountPln" })
        {
            Assert.Equal(JsonValueKind.Number, document.RootElement.GetProperty(name).ValueKind);
            Assert.Equal(amount, document.RootElement.GetProperty(name).GetDecimal());
            Assert.Equal(text, document.RootElement.GetProperty(name + "Exact").GetString());
        }
        Assert.Equal(text, document.RootElement.GetProperty("grossAmount").GetRawText());
        var restored = JsonSerializer.Deserialize<InvoiceDto>(document.RootElement.GetRawText(), new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
        Assert.Equal(amount, restored.GrossAmount);
    }

    [Fact]
    public void PreservesNullAmountsAndDoesNotFormatWithTheCurrentCulture()
    {
        var original = CultureInfo.CurrentCulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("pl-PL");
            using var document = JsonDocument.Parse(JsonSerializer.Serialize(new InvoiceDto { GrossAmount = 1.23m }, new JsonSerializerOptions(JsonSerializerDefaults.Web)));
            Assert.Equal("1.23", document.RootElement.GetProperty("grossAmountExact").GetString());
            foreach (string name in new[] { "netAmountExact", "vatAmountExact", "vatAmountPlnExact" })
                Assert.Equal(JsonValueKind.Null, document.RootElement.GetProperty(name).ValueKind);
        }
        finally { CultureInfo.CurrentCulture = original; }
    }
}
