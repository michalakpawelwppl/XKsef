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

        XElement[] buyers = root.Elements(ns + "Podmiot2").Take(2).ToArray();
        XElement[] buyerIdentifiers = buyers.Length == 1
            ? buyers[0].Elements(ns + "DaneIdentyfikacyjne").Take(2).ToArray() : [];
        XElement? buyerData = buyerIdentifiers.Length == 1 ? buyerIdentifiers[0] : null;
        string? buyerNip = ReadOptionalText(buyerData, ns + "NIP");
        if (buyerNip is not null && (buyerNip.Length != 10 || buyerNip.Any(c => c < '0' || c > '9')))
            buyerNip = null;
        string? buyerName = ReadOptionalText(buyerData, ns + "Nazwa");
        var (netAmount, vatAmount) = ReadSummaryAmounts(root, fa, ns);

        return new InvoiceMetadata
        {
            InvoiceNumber = invoiceNumber,
            IssueDate = issueDate,
            GrossAmount = grossAmount,
            NetAmount = netAmount,
            VatAmount = vatAmount,
            BuyerNip = buyerNip,
            BuyerName = buyerName,
            Currency = currency,
            SellerName = sellerName,
            SellerNip = sellerNip
        };
    }

    // FA(2) and FA(3) use these same document-level summary fields.
    private static readonly string[] TaxableNetFields = ["P_13_1", "P_13_2", "P_13_3", "P_13_4", "P_13_5"];
    private static readonly string[] VatFields = ["P_14_1", "P_14_2", "P_14_3", "P_14_4", "P_14_5"];
    private static readonly string[] NonTaxableNetFields = ["P_13_6_1", "P_13_6_2", "P_13_6_3", "P_13_7", "P_13_8", "P_13_9", "P_13_10"];
    private static readonly HashSet<string> KnownSummaryFields = new(
        TaxableNetFields.Concat(NonTaxableNetFields).Concat(VatFields).Concat(["P_13_11", "P_14_1W", "P_14_2W", "P_14_3W", "P_14_4W"]),
        StringComparer.Ordinal);
    private static readonly HashSet<string> SupportedInvoiceKinds = new(["VAT", "KOR", "ZAL", "KOR_ZAL", "ROZ", "KOR_ROZ"], StringComparer.Ordinal);

    private static (decimal? NetAmount, decimal? VatAmount) ReadSummaryAmounts(XElement root, XElement fa, XNamespace ns)
    {
        if (root.Name.LocalName != "Faktura" || ns.NamespaceName is not ("http://crd.gov.pl/wzor/2023/06/29/12648/" or "http://crd.gov.pl/wzor/2025/06/25/13775/"))
            return (null, null);

        XElement[] kinds = fa.Elements(ns + "RodzajFaktury").Take(2).ToArray();
        if (root.Elements(ns + "Fa").Skip(1).Any() || kinds.Length != 1 || kinds[0].HasElements || !SupportedInvoiceKinds.Contains(kinds[0].Value.Trim()))
            return (null, null);

        // Margin sales do not disclose a reliable net/tax split for this parser.
        if (fa.Elements(ns + "P_13_11").Any()
            || fa.Elements(ns + "Adnotacje").Elements(ns + "PMarzy").Elements(ns + "P_PMarzy").Any(x => x.Value.Trim() == "1"))
            return (null, null);

        if (fa.Elements().Any(x => x.Name.Namespace == ns
            && (x.Name.LocalName.StartsWith("P_13_", StringComparison.Ordinal) || x.Name.LocalName.StartsWith("P_14_", StringComparison.Ordinal))
            && !KnownSummaryFields.Contains(x.Name.LocalName)))
            return (null, null);

        // An orphan tax field could mean the net summary is incomplete.
        for (int index = 0; index < VatFields.Length; index++)
        {
            if (fa.Elements(ns + VatFields[index]).Any() && !fa.Elements(ns + TaxableNetFields[index]).Any())
                return (null, null);
        }

        decimal? netAmount = SumSummaryFields(fa, ns, TaxableNetFields.Concat(NonTaxableNetFields));
        decimal? vatAmount = SumSummaryFields(fa, ns, VatFields);
        bool hasTaxableBase = TaxableNetFields.Any(field => fa.Elements(ns + field).Any());
        bool hasVatField = VatFields.Any(field => fa.Elements(ns + field).Any());

        for (int index = 0; index < TaxableNetFields.Length; index++)
        {
            if (fa.Elements(ns + TaxableNetFields[index]).Any() && !fa.Elements(ns + VatFields[index]).Any())
                vatAmount = null;
        }

        // Zero is justified only by an explicit, valid zero-rated/exempt/non-taxable summary.
        if (!hasTaxableBase && !hasVatField && netAmount.HasValue)
            vatAmount = 0m;

        return (netAmount, vatAmount);
    }

    private static decimal? SumSummaryFields(XElement fa, XNamespace ns, IEnumerable<string> fields)
    {
        decimal? sum = null;
        foreach (string field in fields)
        {
            XElement[] elements = fa.Elements(ns + field).Take(2).ToArray();
            if (elements.Length == 0) continue;
            if (elements.Length != 1 || elements[0].HasElements) return null;

            string text = elements[0].Value.Trim();
            int decimalPoint = text.IndexOf('.');
            if (decimalPoint >= 0 && text.Length - decimalPoint - 1 > 2) return null;
            if (!decimal.TryParse(text, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out decimal value))
                return null;

            try
            {
                sum = checked((sum ?? 0m) + value);
            }
            catch (OverflowException)
            {
                return null;
            }
        }
        return sum;
    }

    private static string? ReadOptionalText(XElement? parent, XName name)
    {
        XElement[] elements = parent?.Elements(name).Take(2).ToArray() ?? [];
        if (elements.Length != 1 || elements[0].HasElements) return null;
        string value = elements[0].Value.Trim();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}