using System.Globalization;
using System.Numerics;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace xKsef.Domain.SalesInvoices;

// Application discriminant of FA(3)'s optional choice, not the XML marker's numeric value.
// null means no declaration of payment; it must not be interpreted as paid=false.
public enum InvoicePaymentStatus { PaidOnce = 1, PartiallyPaid = 2, FullyPaidInParts = 3 }
public sealed record InvoicePartialPayment(decimal? Amount = null, DateOnly? PaymentDate = null, int? PaymentMethod = null, string? OtherPaymentDescription = null);
public sealed record InvoiceCashDiscount(string? Conditions = null, string? AmountDescription = null);

// xsd:integer has no Int32 limit. Keep exact quantities in JSON, accepting historical numeric tokens too.
public sealed class InvoicePaymentTermQuantityConverter : JsonConverter<BigInteger?>
{
    public override BigInteger? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        using var value = JsonDocument.ParseValue(ref reader);
        if (value.RootElement.ValueKind == JsonValueKind.Null) return null;
        var text = value.RootElement.ValueKind switch { JsonValueKind.String => value.RootElement.GetString(), JsonValueKind.Number => value.RootElement.GetRawText(), _ => null };
        text = text?.Trim(' ', '\t', '\r', '\n');
        if (text is null || !Regex.IsMatch(text, @"\A[+-]?[0-9]+\z") || !BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var quantity)) throw new JsonException("Nieprawidłowa całkowita ilość terminu płatności.");
        return quantity;
    }
    public override void Write(Utf8JsonWriter writer, BigInteger? value, JsonSerializerOptions options)
    {
        if (value is null) writer.WriteNullValue(); else writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
    }
}
