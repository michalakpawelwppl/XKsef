using KSeF.Client.Core.Models.Invoices;
using System.Text.Json;
using System.Text.Json.Serialization;
using xKsef.Application.Invoices;
// using przestrzeni nazw InvoicePackageMetadata

namespace xKsef.Infrastructure.Invoices;

internal static class InvoicePackageMetadataReader
{
    public static InvoicePackageMetadata Read(string json)
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }
        };

        return JsonSerializer.Deserialize<InvoicePackageMetadata>(json, options) ?? throw new InvalidOperationException("Nie udało się odczytać metadanych paczki KSeF.");
    }

    public static IReadOnlyList<ExportedInvoiceMetadata> ReadInvoices(string json)
    {
        var metadata = Read(json);

        if (metadata.Invoices is null)
            throw new InvalidOperationException("Metadane paczki nie zawierają listy faktur.");

        // SDK value types cannot distinguish an omitted JSON field from its default value.
        using var document = JsonDocument.Parse(json);
        var entries = document.RootElement.EnumerateObject()
            .Last(property => string.Equals(property.Name, "invoices", StringComparison.OrdinalIgnoreCase))
            .Value.EnumerateArray().ToArray();

        return metadata.Invoices.Select((invoice, index) => InvoiceMetadataMapper.Map(
            invoice,
            HasValue(entries[index], "vatAmount"),
            HasValue(entries[index], "invoicingDate"))).ToList();
    }

    private static bool HasValue(JsonElement invoice, string name)
    {
        return invoice.EnumerateObject().Any(property =>
            string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
            && property.Value.ValueKind != JsonValueKind.Null);
    }

    public static async Task<IReadOnlyList<ExportedInvoiceMetadata>> ReadFromFileAsync(string filePath,CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);

        return ReadInvoices(json);
    }

}