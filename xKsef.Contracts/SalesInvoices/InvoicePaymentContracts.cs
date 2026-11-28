using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace xKsef.Contracts.SalesInvoices;

public sealed record PartialPaymentDto(ExactDecimal? Amount = null, DateOnly? PaymentDate = null, int? PaymentMethod = null, string? OtherPaymentDescription = null);
public sealed record CashDiscountDto(string? Conditions = null, string? AmountDescription = null);

// API integer transport is independent of the approval snapshot contract.
public sealed class PaymentTermQuantityConverter : JsonConverter<BigInteger?>
{
    public override BigInteger? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var value = JsonDocument.ParseValue(ref reader);
        if (value.RootElement.ValueKind == JsonValueKind.Null) return null;
        var text = value.RootElement.ValueKind switch { JsonValueKind.String => value.RootElement.GetString(), JsonValueKind.Number => value.RootElement.GetRawText(), _ => null };
        text = text?.Trim(' ', '\t', '\r', '\n');
        if (text is null || !Regex.IsMatch(text, @"\A[+-]?[0-9]+\z") || !BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var quantity)) throw new JsonException("Podaj całkowitą ilość terminu płatności.");
        return quantity;
    }
    public override void Write(Utf8JsonWriter writer, BigInteger? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
    }
}
