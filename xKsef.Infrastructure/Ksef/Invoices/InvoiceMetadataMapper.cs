using KSeF.Client.Core.Models.Invoices;
using xKsef.Application.Invoices;
// Dodaj using przestrzeni nazw InvoiceSummary.

namespace xKsef.Infrastructure.Invoices;

internal static class InvoiceMetadataMapper
{
    public static ExportedInvoiceMetadata Map(InvoiceSummary source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (string.IsNullOrWhiteSpace(source.KsefNumber))
        {
            throw new InvalidOperationException(
                "Metadane faktury nie zawierają numeru KSeF.");
        }

        return new ExportedInvoiceMetadata
        {
            KsefNumber = source.KsefNumber,
            FileName = $"{source.KsefNumber}.xml",
            InvoiceHash = source.InvoiceHash
        };
    }

    public static IReadOnlyList<ExportedInvoiceMetadata> Map(
    InvoicePackageMetadata source)
    {
        ArgumentNullException.ThrowIfNull(source);

        if (source.Invoices is null)
        {
            throw new InvalidOperationException(
                "Metadane paczki nie zawierają listy faktur.");
        }

        return source.Invoices
            .Select(Map)
            .ToList();
    }
}