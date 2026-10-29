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

        return InvoiceMetadataMapper.Map(metadata);
    }

    public static async Task<IReadOnlyList<ExportedInvoiceMetadata>> ReadFromFileAsync(string filePath,CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(filePath, cancellationToken);

        return ReadInvoices(json);
    }

}