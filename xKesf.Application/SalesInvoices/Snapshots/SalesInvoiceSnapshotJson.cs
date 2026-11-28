using System.Text.Json;
using System.Text.Json.Serialization;

namespace xKsef.Application.SalesInvoices.Snapshots;

internal static class SalesInvoiceSnapshotJson
{
    internal static readonly JsonSerializerOptions Options = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions { UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow };
        options.Converters.Add(new ReadOnlyListConverterFactory());
        return options;
    }

    internal static T Read<T>(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(json);
            RejectDuplicateProperties(document.RootElement);
            return JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException();
        }
        catch (Exception error) when (error is JsonException or NotSupportedException or ArgumentException or OverflowException)
        {
            // Do not expose JSON, tax identifiers or serializer diagnostics in the public error.
            throw new SalesInvoiceApprovalSnapshotException("SnapshotInvalid", "Snapshot zatwierdzenia jest uszkodzony lub niezgodny ze swoim formatem.");
        }
    }

    internal static bool Same(object? left, object? right) => JsonSerializer.Serialize(left, Options) == JsonSerializer.Serialize(right, Options);

    private static void RejectDuplicateProperties(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in element.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException();
                RejectDuplicateProperties(property.Value);
            }
        }
        else if (element.ValueKind == JsonValueKind.Array)
            foreach (var child in element.EnumerateArray()) RejectDuplicateProperties(child);
    }

    private sealed class ReadOnlyListConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type type) => type.IsGenericType && type.GetGenericTypeDefinition() == typeof(IReadOnlyList<>);
        public override JsonConverter CreateConverter(Type type, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(ReadOnlyListConverter<>).MakeGenericType(type.GetGenericArguments()[0]))!;
    }

    private sealed class ReadOnlyListConverter<T> : JsonConverter<IReadOnlyList<T>>
    {
        public override IReadOnlyList<T>? Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) =>
            JsonSerializer.Deserialize<T[]>(ref reader, options) is { } values ? Array.AsReadOnly(values) : null;
        public override void Write(Utf8JsonWriter writer, IReadOnlyList<T> value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var item in value) JsonSerializer.Serialize(writer, item, options);
            writer.WriteEndArray();
        }
    }
}
