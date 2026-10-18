using KSeF.Client.Core.Models.Invoices;
using System.Text.Json;
using xKsef.Application.Invoices;
// using przestrzeni nazw InvoicePackageMetadata

namespace xKsef.Infrastructure.Invoices;

internal static class InvoicePackageMetadataReader
{
    public static InvoicePackageMetadata Read(string json)
    {
        return JsonSerializer.Deserialize<InvoicePackageMetadata>(
            json,
            new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            })
            ?? throw new InvalidOperationException(
                "Nie udało się odczytać metadanych paczki KSeF.");
    }

    public static IReadOnlyList<ExportedInvoiceMetadata> ReadInvoices(
    string json)
    {
        var metadata = Read(json);

        return InvoiceMetadataMapper.Map(metadata);
    }

    public static async Task<IReadOnlyList<ExportedInvoiceMetadata>>
    ReadFromFileAsync(
        string filePath,
        CancellationToken cancellationToken = default)
    {
        var json = await File.ReadAllTextAsync(
            filePath,
            cancellationToken);

        return ReadInvoices(json);
    }

}