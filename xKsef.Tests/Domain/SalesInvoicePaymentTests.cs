using System.Numerics;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Tests.Domain;

public sealed class SalesInvoicePaymentTests
{
    private const string Id = "001ABC123DEF4";
    private const string Link = "https://pay.example/pay?IPKSeF=" + Id;
    private static readonly DateOnly PaidDate = new(2026, 11, 28);
    private static readonly Lazy<XmlSchemaSet> Schemas = new(LoadSchemas);
    private static InvoiceValidationIssue[] Errors(SalesInvoiceDetailsData details, bool complete = true)
    {
        var issues = new List<InvoiceValidationIssue>();
        SalesInvoicePaymentValidator.Validate(new() { PlannedIssueDate = new(2026, 12, 1), Details = details }, issues, complete);
        return issues.ToArray();
    }

    [Fact]
    public void AbsentPaymentDeclarationDoesNotBecomeFalseOrZero()
    {
        var details = new SalesInvoiceDetailsData(); Assert.Empty(Errors(details));
        Assert.Null(details.PaymentStatus); Assert.Null(details.PaymentDate); Assert.Empty(details.PartialPayments);
        Assert.Null(details.CashDiscount); Assert.Null(details.PaymentLink); Assert.Null(details.KsefPaymentId);
        Assert.Empty(Errors(new() { PaymentMethod = 6 })); // Future bank transfer, without a declaration of receipt.
        Assert.Contains(Errors(new() { PaymentStatus = (InvoicePaymentStatus)0 }), x => x.Code == "PaymentStatusInvalid");
    }

    [Fact]
    public void SingleAndPartialPaymentsUseExclusiveBranchesAndAllowIncompleteDrafts()
    {
        var partial = new InvoicePartialPayment(10m, PaidDate, 6);
        Assert.Empty(Errors(new() { PaymentStatus = InvoicePaymentStatus.PaidOnce, PaymentDate = PaidDate }));
        Assert.Empty(Errors(new() { PaymentStatus = InvoicePaymentStatus.PartiallyPaid, PartialPayments = [partial] }));
        Assert.Empty(Errors(new() { PaymentStatus = InvoicePaymentStatus.FullyPaidInParts, PartialPayments = [partial, partial with { PaymentMethod = 2 }] }));
        var incomplete = new SalesInvoiceDetailsData { PaymentStatus = InvoicePaymentStatus.PaidOnce, PaymentMethod = 8, CashDiscount = new() };
        SalesInvoiceValidator.Validate(new() { Details = incomplete });
        Assert.Contains(Errors(incomplete), x => x.Code == "PaymentDateRequired"); Assert.Contains(Errors(incomplete), x => x.Code == "PaymentFieldRequired");
        Assert.Contains(Errors(new() { PaymentStatus = InvoicePaymentStatus.PaidOnce, PaymentDate = PaidDate, PartialPayments = [partial] }), x => x.Code == "PaymentChoiceConflict");
        Assert.Contains(Errors(new() { PartialPayments = [partial] }), x => x.Code == "PaymentStatusRequired");
        Assert.Contains(Errors(new() { PaymentStatus = InvoicePaymentStatus.FullyPaidInParts, PartialPayments = [partial] }), x => x.Code == "FinalPaymentPartsRequired");
        Assert.Contains(Errors(new() { PaymentStatus = InvoicePaymentStatus.PartiallyPaid }), x => x.Code == "PartialPaymentsRequired");
        Assert.Contains(Errors(new() { PaymentDate = PaidDate }), x => x.Code == "PaymentChoiceConflict");
    }

    [Theory]
    [InlineData(1)] [InlineData(2)] [InlineData(3)] [InlineData(4)] [InlineData(5)] [InlineData(6)] [InlineData(7)] [InlineData(8)]
    public void ExistingMethodCodesAlsoWorkForEveryPayment(int method)
    {
        var description = method == 8 ? "Inna forma" : null;
        Assert.Empty(Errors(new() { PaymentMethod = method, OtherPaymentDescription = description, PaymentStatus = InvoicePaymentStatus.PartiallyPaid, PartialPayments = [new(10, PaidDate, method, description)] }));
        Assert.Contains(Errors(new() { PaymentMethod = 9 }), x => x.Code == "PaymentMethodInvalid");
        Assert.Contains(Errors(new() { PaymentMethod = 6, OtherPaymentDescription = "Inna" }), x => x.Code == "PaymentMethodConflict");
    }

    [Theory]
    [InlineData(1, true)] [InlineData(100, true)] [InlineData(101, false)]
    public void PartialPaymentsUseXsdMultiplicity(int count, bool valid)
    {
        var details = new SalesInvoiceDetailsData { PaymentStatus = InvoicePaymentStatus.PartiallyPaid, PartialPayments = Enumerable.Repeat(new InvoicePartialPayment(0m, PaidDate), count).ToArray() };
        Assert.Equal(valid, Errors(details).Length == 0);
    }

    [Fact]
    public void AmountsDatesAndConditionalDescriptionsAreCheckedWithoutAnOutstandingBalanceAlgorithm()
    {
        // Signed TKwotowy and overpayments are stored, not reinterpreted or rejected against a guessed payable amount.
        Assert.Empty(Errors(new() { PaymentStatus = InvoicePaymentStatus.PartiallyPaid, PartialPayments = [new(-1m, PaidDate), new(FinancialMath.MaxAmount, PaidDate)] }));
        var issues = Errors(new() { PaymentStatus = InvoicePaymentStatus.PartiallyPaid, PartialPayments = [new(.001m, new(2026, 12, 2), 8), new(null, null)] });
        foreach (var code in new[] { "PartialPaymentAmountInvalid", "PaymentAfterIssueDate", "PaymentFieldRequired", "PartialPaymentAmountRequired", "PartialPaymentDateRequired" }) Assert.Contains(issues, x => x.Code == code);
        Assert.Contains(Errors(new() { PaymentDate = new(2016, 6, 30) }, false), x => x.Code == "InvalidPaymentDate");
        Assert.Contains(Errors(new() { PaymentDate = new(2050, 1, 2) }, false), x => x.Code == "InvalidPaymentDate");
        Assert.Empty(Errors(new() { PaymentStatus = InvoicePaymentStatus.PaidOnce, PaymentDate = PaidDate, PaymentTerms = [new(new(2016, 7, 1))] }));
    }

    [Fact]
    public void TermsKeepTheirMeaningAndSupportTheUnboundedSignedXsdInteger()
    {
        var large = BigInteger.Parse("1234567890123456789012345678901234567890");
        foreach (var quantity in new[] { large, BigInteger.Zero, -BigInteger.One })
        {
            var term = new InvoicePaymentTerm(null, quantity, "dni", "data wystawienia");
            Assert.Empty(Errors(new() { PaymentTerms = [term] }));
            Assert.Equal(term, JsonSerializer.Deserialize<InvoicePaymentTerm>(JsonSerializer.Serialize(term)));
        }
        Assert.Empty(Errors(new() { PaymentTerms = [new(null)] })); // Empty TerminPlatnosci is permitted by XSD.
        Assert.Contains(Errors(new() { PaymentTerms = [new(null, 14)] }), x => x.Code == "PaymentTermDescriptionIncomplete");
        Assert.Empty(Errors(new() { PaymentTerms = [new(null, 14)] }, false));
        Assert.Equal(new BigInteger(14), JsonSerializer.Deserialize<InvoicePaymentTerm>("{\"DueDate\":null,\"Quantity\":14}")!.Quantity);
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<InvoicePaymentTerm>("{\"Quantity\":1.5}"));
    }

    [Fact]
    public void CashDiscountAndPaymentIdentifiersMatchXsdWithoutRequiringEitherIdentifierField()
    {
        Assert.Empty(Errors(new() { CashDiscount = new("Zapłata do 7 dni", "3% kwoty brutto"), PaymentLink = Link, KsefPaymentId = Id }));
        Assert.Empty(Errors(new() { PaymentLink = Link })); Assert.Empty(Errors(new() { KsefPaymentId = Id }));
        var maxLink = Link + "#" + new string('x', 511 - Link.Length);
        Assert.Equal(512, maxLink.Length); Assert.Empty(Errors(new() { PaymentLink = maxLink }));
        Assert.Contains(Errors(new() { PaymentLink = maxLink + "x" }), x => x.Code == "PaymentTextInvalid");
        foreach (var link in new[] { "https://pay.example/pay", "ftp://pay.example/?IPKSeF=" + Id, "http://localhost/?IPKSeF=" + Id }) Assert.Contains(Errors(new() { PaymentLink = link }), x => x.Code == "PaymentLinkInvalid");
        Assert.Contains(Errors(new() { KsefPaymentId = "ABC123DEF4567" }), x => x.Code == "KsefPaymentIdInvalid");
        Assert.Contains(Errors(new() { PaymentLink = Link, KsefPaymentId = "002ABC123DEF4" }), x => x.Code == "PaymentIdentifierMismatch");
        Assert.Contains(Errors(new() { CashDiscount = new("Warunek") }), x => x.Code == "PaymentFieldRequired");
        Assert.Empty(Errors(new() { CashDiscount = new(new string('x', 256), new string('y', 256)) }));
        Assert.Contains(Errors(new() { CashDiscount = new(new string('x', 257), "Wysokość") }), x => x.Code == "PaymentTextInvalid");
    }

    [Fact]
    public void ActualPaymentXsdConfirmsChoicesFormatsAndMarkers()
    {
        XNamespace ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";
        XElement Part(decimal amount, DateOnly date) => new(ns + "ZaplataCzesciowa", new XElement(ns + "KwotaZaplatyCzesciowej", amount), new XElement(ns + "DataZaplatyCzesciowej", date.ToString("yyyy-MM-dd")));
        bool Valid(params object[] children) { var valid = true; new XDocument(new XElement(ns + "PaymentTest", children)).Validate(Schemas.Value, (_, _) => valid = false); return valid; }
        Assert.True(Valid()); Assert.True(Valid(new XElement(ns + "Zaplacono", 1), new XElement(ns + "DataZaplaty", "2026-11-28")));
        Assert.False(Valid(new XElement(ns + "Zaplacono", 0), new XElement(ns + "DataZaplaty", "2026-11-28")));
        Assert.False(Valid(new XElement(ns + "Zaplacono", 1)));
        Assert.True(Valid(new XElement(ns + "ZnacznikZaplatyCzesciowej", 1), Part(10m, PaidDate)));
        Assert.True(Valid(new XElement(ns + "ZnacznikZaplatyCzesciowej", 2), Part(10m, PaidDate))); // XSD allows one; MF semantics require two for marker 2.
        Assert.False(Valid(new XElement(ns + "ZnacznikZaplatyCzesciowej", 1)));
        Assert.False(Valid(new XElement(ns + "ZnacznikZaplatyCzesciowej", 1), Part(.001m, PaidDate)));
        Assert.False(Valid(new XElement(ns + "FormaPlatnosci", 8)));
        Assert.True(Valid(new XElement(ns + "PlatnoscInna", 1), new XElement(ns + "OpisPlatnosci", "Inna")));
        Assert.True(Valid(new XElement(ns + "Skonto", new XElement(ns + "WarunkiSkonta", "7 dni"), new XElement(ns + "WysokoscSkonta", "3%")), new XElement(ns + "LinkDoPlatnosci", Link), new XElement(ns + "IPKSeF", Id)));
        Assert.False(Valid(new XElement(ns + "LinkDoPlatnosci", "https://pay.example/")));
    }

    private static XmlSchemaSet LoadSchemas()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "XKsef.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory); XNamespace xs = XmlSchema.Namespace;
        var schemas = new XmlSchemaSet { XmlResolver = null };
        foreach (var file in new[] { "schemat.xsd", "StrukturyDanych_v10-0E.xsd", "ElementarneTypyDanych_v10-0E.xsd", "KodyKrajow_v10-0E.xsd" })
        {
            using var reader = XmlReader.Create(Path.Combine(directory.FullName, "docs", "analysis", "fa3-sources", file), new() { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit });
            var document = XDocument.Load(reader);
            if (file == "schemat.xsd")
            {
                var payment = new XElement(document.Descendants(xs + "element").Single(x => (string?)x.Attribute("name") == "Platnosc"));
                payment.SetAttributeValue("name", "PaymentTest"); payment.Attribute("minOccurs")!.Remove(); document.Root!.Add(payment);
            }
            using var localReader = document.CreateReader(); var schema = XmlSchema.Read(localReader, null)!;
            foreach (var include in schema.Includes.OfType<XmlSchemaInclude>().ToArray()) schema.Includes.Remove(include);
            foreach (XmlSchemaExternal external in schema.Includes) external.SchemaLocation = null;
            schemas.Add(schema);
        }
        schemas.Compile(); return schemas;
    }
}
