using System.Globalization;
using System.Xml.Linq;
using xKsef.Application.Invoices.Visualization;

namespace xKsef.Infrastructure.Invoices.Visualization;

public class KsefInvoiceXmlParser : IInvoiceXmlParser
{
    public InvoiceVisualizationModel Parse(string xml)
    {
        XDocument document = XDocument.Parse(xml);

        XElement root = document.Root
            ?? throw new InvalidOperationException(
                "XML nie zawiera elementu głównego.");

        XNamespace ns = root.Name.Namespace;

        string invoiceNumber = root
            .Element(ns + "Fa")?
            .Element(ns + "P_2")?
            .Value
            ?? string.Empty;

        string? issueDateText = root
    .Element(ns + "Fa")?
    .Element(ns + "P_1")?
    .Value;

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

        XElement? sellerData = root
    .Element(ns + "Podmiot1")?
    .Element(ns + "DaneIdentyfikacyjne");

        string sellerNip = sellerData?
            .Element(ns + "NIP")?
            .Value ?? string.Empty;

        string sellerName = sellerData?
            .Element(ns + "Nazwa")?
            .Value ?? string.Empty;

        XElement? buyerData = root
    .Element(ns + "Podmiot2")?
    .Element(ns + "DaneIdentyfikacyjne");

        string buyerNip = buyerData?
            .Element(ns + "NIP")?
            .Value ?? string.Empty;

        string buyerName = buyerData?
            .Element(ns + "Nazwa")?
            .Value ?? string.Empty;

        string? grossAmountText = root
    .Element(ns + "Fa")?
    .Element(ns + "P_15")?
    .Value;

        if (!decimal.TryParse(
                grossAmountText,
                NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out decimal grossAmount))
        {
            throw new FormatException(
                "Nieprawidłowa wartość brutto faktury.");
        }

        string currency = root
    .Element(ns + "Fa")?
    .Element(ns + "KodWaluty")?
    .Value
    ?? string.Empty;

        var items = new List<InvoiceItemVisualizationModel>();

        var invoiceRows = root
            .Element(ns + "Fa")?
            .Elements(ns + "FaWiersz")
            ?? Enumerable.Empty<XElement>();

        foreach (var row in invoiceRows)
        {
            string? lineNumberText = row
                .Element(ns + "NrWierszaFa")?
                .Value;

            if (!int.TryParse(lineNumberText, out int lineNumber))
            {
                throw new FormatException(
                    "Nieprawidłowy numer pozycji faktury.");
            }
            string description = row
    .Element(ns + "P_7")?
    .Value ?? string.Empty;
            string? quantityText = row
    .Element(ns + "P_8B")?
    .Value;

            decimal? quantity = null;

            if (!string.IsNullOrWhiteSpace(quantityText))
            {
                if (!decimal.TryParse(
                        quantityText,
                        NumberStyles.AllowDecimalPoint |
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out decimal parsedQuantity))
                {
                    throw new FormatException(
                        "Nieprawidłowa ilość w pozycji faktury.");
                }

                quantity = parsedQuantity;
            }
            string unit = row
    .Element(ns + "P_8A")?
    .Value ?? string.Empty;

            string? netUnitPriceText = row
    .Element(ns + "P_9A")?
    .Value;

            decimal? netUnitPrice = null;

            if (!string.IsNullOrWhiteSpace(netUnitPriceText))
            {
                if (!decimal.TryParse(
                        netUnitPriceText,
                        NumberStyles.AllowDecimalPoint |
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out decimal parsedPrice))
                {
                    throw new FormatException(
                        "Nieprawidłowa cena jednostkowa netto.");
                }

                netUnitPrice = parsedPrice;
            }

            
            string? netAmountText = row
    .Element(ns + "P_11")?
    .Value;

            decimal? netAmount = null;

            if (!string.IsNullOrWhiteSpace(netAmountText))
            {
                if (!decimal.TryParse(
                        netAmountText,
                        NumberStyles.AllowDecimalPoint |
                        NumberStyles.AllowLeadingSign,
                        CultureInfo.InvariantCulture,
                        out decimal parsedAmount))
                {
                    throw new FormatException(
                        "Nieprawidłowa wartość netto pozycji faktury.");
                }

                netAmount = parsedAmount;
            }
            string vatRate = row
    .Element(ns + "P_12")?
    .Value ?? string.Empty;

            items.Add(new InvoiceItemVisualizationModel
            {
                LineNumber = lineNumber,
                Description = description,
                Quantity = quantity,
                Unit = unit,
                NetUnitPrice = netUnitPrice,
                NetAmount = netAmount,
                VatRate = vatRate
            });

        }

        return new InvoiceVisualizationModel
        {
            InvoiceNumber = invoiceNumber,
            IssueDate = issueDate,
            SellerName = sellerName,
            SellerNip = sellerNip,
            BuyerName = buyerName,
            BuyerNip = buyerNip,
            GrossAmount = grossAmount,
            Currency = currency,
            Items = items,
           
        };
    }
}