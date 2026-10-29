using System.Globalization;
using System.Xml.Linq;
using xKsef.Application.Invoices;

namespace xKsef.Infrastructure.Invoices;

public sealed class KsefInvoiceMetadataParser : IInvoiceMetadataParser
{
    public InvoiceMetadata Parse(byte[] xmlBytes)
    {
        ArgumentNullException.ThrowIfNull(xmlBytes);

        using var stream = new MemoryStream(xmlBytes);

        XDocument document = XDocument.Load(stream);

        XElement root = document.Root
            ?? throw new InvalidOperationException(
                "XML faktury nie zawiera elementu głównego.");

        XNamespace ns = root.Name.Namespace;
        XElement fa = root.Element(ns + "Fa")
    ?? throw new FormatException(
        "XML faktury nie zawiera elementu Fa.");

        string invoiceNumber = fa.Element(ns + "P_2")?.Value
            ?? throw new FormatException(
                "Brak numeru faktury.");

        string? issueDateText = fa.Element(ns + "P_1")?.Value;

        if (!DateOnly.TryParseExact(
                issueDateText,
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out DateOnly issueDate))
        {
            throw new FormatException(
                "Nieprawidłowa data wystawienia faktury.");
        }

        XElement sellerData = root
    .Element(ns + "Podmiot1")?
    .Element(ns + "DaneIdentyfikacyjne")
    ?? throw new FormatException(
        "Brak danych identyfikacyjnych sprzedawcy.");

        string sellerNip = sellerData
            .Element(ns + "NIP")?
            .Value ?? string.Empty;

        string sellerName = sellerData
            .Element(ns + "Nazwa")?
            .Value ?? string.Empty;

        string? grossAmountText = fa
    .Element(ns + "P_15")?
    .Value;

        if (!decimal.TryParse(
                grossAmountText,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out decimal grossAmount))
        {
            throw new FormatException(
                "Nieprawidłowa kwota brutto faktury.");
        }
        string currency = fa
    .Element(ns + "KodWaluty")?
    .Value ?? throw new FormatException(
        "Brak kodu waluty faktury.");

        return new InvoiceMetadata
        {
            InvoiceNumber = invoiceNumber,
            IssueDate = issueDate,
            GrossAmount = grossAmount,
            Currency = currency,
            SellerName = sellerName,
            SellerNip = sellerNip
        };
    }
}