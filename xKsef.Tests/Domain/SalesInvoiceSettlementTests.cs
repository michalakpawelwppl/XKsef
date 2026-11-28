using System.Xml;
using System.Xml.Linq;
using System.Xml.Schema;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Tests.Domain;

public sealed class SalesInvoiceSettlementTests
{
    [Fact]
    public void MfExampleAndFacetsValidateAgainstTheActualLocalSettlementXsd()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "XKsef.slnx"))) directory = directory.Parent;
        Assert.NotNull(directory);
        using var reader = XmlReader.Create(Path.Combine(directory.FullName, "docs", "analysis", "fa3-sources", "schemat.xsd"), new() { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit });
        var source = XDocument.Load(reader); XNamespace xs = XmlSchema.Namespace; XNamespace ns = "http://crd.gov.pl/wzor/2025/06/25/13775/";
        var section = new XElement(source.Descendants(xs + "element").Single(x => (string?)x.Attribute("name") == "Rozliczenie"));
        Assert.Equal("0", (string?)section.Attribute("minOccurs")); section.Attribute("minOccurs")!.Remove();
        foreach (var name in new[] { "Obciazenia", "Odliczenia" })
            Assert.Equal("100", (string?)section.Descendants(xs + "element").Single(x => (string?)x.Attribute("name") == name).Attribute("maxOccurs"));
        var schema = new XElement(xs + "schema", new XAttribute("targetNamespace", ns.NamespaceName), new XAttribute("elementFormDefault", "qualified"),
            new XAttribute(XNamespace.Xmlns + "xsd", xs.NamespaceName), new XAttribute(XNamespace.Xmlns + "tns", ns.NamespaceName),
            source.Root!.Elements(xs + "simpleType").Where(x => (string?)x.Attribute("name") is "TKwotowy" or "TZnakowy").Select(x => new XElement(x)), section);
        var schemas = new XmlSchemaSet { XmlResolver = null }; using var schemaReader = schema.CreateReader(); schemas.Add(ns.NamespaceName, schemaReader); schemas.Compile();
        XElement Entry(string name, decimal amount, string? reason) => new(ns + name, new XElement(ns + "Kwota", amount), reason is null ? null : new XElement(ns + "Powod", reason));
        bool Valid(XElement value) { var valid = true; new XDocument(value).Validate(schemas, (_, _) => valid = false); return valid; }
        Assert.True(Valid(new(ns + "Rozliczenie", Entry("Obciazenia", 17m, "Zwrot kosztów"), new XElement(ns + "SumaObciazen", 17), Entry("Odliczenia", 300m, "Saldo"), new XElement(ns + "SumaOdliczen", 300), new XElement(ns + "DoRozliczenia", 37))));
        Assert.True(Valid(new(ns + "Rozliczenie"))); Assert.True(Valid(new(ns + "Rozliczenie", Entry("Obciazenia", -1m, "Powód"))));
        Assert.False(Valid(new(ns + "Rozliczenie", Entry("Obciazenia", .001m, "Powód"))));
        Assert.False(Valid(new(ns + "Rozliczenie", Entry("Odliczenia", 1m, null))));
        Assert.False(Valid(new(ns + "Rozliczenie", new XElement(ns + "DoZaplaty", 0), new XElement(ns + "DoRozliczenia", 0))));
        Assert.False(Valid(new(ns + "Rozliczenie", Enumerable.Range(0, 101).Select(_ => Entry("Obciazenia", 0m, "Powód")))));
    }
    [Theory]
    [InlineData(246, 17, 300, null, 37)] // MF brochure, example 24.
    [InlineData(246, 17, 20, 243, null)]
    [InlineData(246, 0, 246, 0, null)]
    [InlineData(246, 0, 0, 246, null)]
    [InlineData(246, -17, -20, 249, null)] // TKwotowy has no non-negative facet.
    public void SettlementUsesInvoiceGrossWithoutChangingVat(int gross, int charge, int deduction, int? due, int? overpayment)
    {
        var input = new InvoiceSettlementData { Charges = [new(charge, "Obciążenie")], Deductions = [new(deduction, "Odliczenie")] };
        var result = InvoiceSettlementCalculator.Calculate(input, gross)!;
        Assert.Equal(charge, result.ChargesTotal); Assert.Equal(deduction, result.DeductionsTotal);
        Assert.Equal(due is null ? null : (decimal?)due, result.AmountDue);
        Assert.Equal(overpayment is null ? null : (decimal?)overpayment, result.Overpayment);
        var errors = new List<InvoiceValidationIssue>(); InvoiceSettlementCalculator.ValidateForIssuance(input, gross, result, errors); Assert.Empty(errors);
    }

    [Fact]
    public void NoSectionEmptySectionAndIncompleteDraftRemainDistinct()
    {
        Assert.Null(InvoiceSettlementCalculator.Calculate(null, 246m));
        Assert.Equal(new(0, 0, 246, null), InvoiceSettlementCalculator.Calculate(new(), 246m));
        var input = new InvoiceSettlementData { Charges = [new(null, null)], Deductions = [new(1m, null)] };
        SalesInvoiceValidator.Validate(new() { Details = new() { Settlement = input } });
        var result = InvoiceSettlementCalculator.Calculate(input, null)!;
        Assert.Null(result.ChargesTotal); Assert.Equal(1m, result.DeductionsTotal); Assert.Null(result.AmountDue); Assert.Null(result.Overpayment);
        var errors = new List<InvoiceValidationIssue>(); InvoiceSettlementCalculator.ValidateForIssuance(input, null, result, errors);
        Assert.Contains(errors, x => x.Code == "SettlementAmountRequired"); Assert.Equal(2, errors.Count(x => x.Code == "SettlementReasonRequired"));
        Assert.Contains(errors, x => x.Code == "SettlementIncomplete");
    }

    [Fact]
    public void MonetaryInputsAreNotSilentlyRoundedAndSumsRemainExact()
    {
        var invalid = new InvoiceSettlementData { Charges = [new(0.005m, "Powód")] };
        Assert.Contains(Assert.Throws<SalesInvoiceValidationException>(() => InvoiceSettlementCalculator.Calculate(invalid, 1m)).Issues, x => x.Code == "SettlementAmountInvalid");
        var input = new InvoiceSettlementData { Charges = [new(.01m, "A"), new(.02m, "B")], Deductions = [new(.02m, "C")] };
        Assert.Equal(new(.03m, .02m, 1.01m, null), InvoiceSettlementCalculator.Calculate(input, 1m));
        Assert.Equal(new(.03m, .04m, null, .01m), InvoiceSettlementCalculator.Calculate(input with { Deductions = [new(.04m, "C")] }, 0m));
    }

    [Theory]
    [InlineData(100, true)] [InlineData(101, false)]
    public void BothCollectionsHaveOneHundredEntryLimit(int count, bool valid)
    {
        foreach (var charges in new[] { true, false })
        {
            var entries = Enumerable.Repeat(new InvoiceSettlementEntry(0m, "Powód"), count).ToArray();
            var input = charges ? new InvoiceSettlementData { Charges = entries } : new InvoiceSettlementData { Deductions = entries };
            if (valid) Assert.NotNull(InvoiceSettlementCalculator.Calculate(input, 0m));
            else Assert.Throws<SalesInvoiceValidationException>(() => InvoiceSettlementCalculator.Calculate(input, 0m));
        }
    }

    [Fact]
    public void AmountRangeReasonLengthAndXmlCharactersAreValidated()
    {
        var issues = new List<InvoiceValidationIssue>();
        InvoiceSettlementCalculator.ValidateInput(new() { Charges = [new(FinancialMath.MaxAmount + .01m, new string('x', 257)), new(1m, "Opis\u0001"), null!] }, issues);
        Assert.Contains(issues, x => x.Code == "SettlementAmountInvalid"); Assert.Contains(issues, x => x.Code == "SettlementReasonTooLong");
        Assert.Contains(issues, x => x.Code == "InvalidXmlCharacters"); Assert.Contains(issues, x => x.Code == "SettlementEntryRequired");
        issues.Clear(); InvoiceSettlementCalculator.ValidateInput(new() { Charges = null! }, issues);
        Assert.Contains(issues, x => x.Code == "SettlementCollectionInvalid");
        Assert.NotNull(InvoiceSettlementCalculator.Calculate(new() { Charges = [new(-FinancialMath.MaxAmount, "Powód")] }, 0m));
    }

    [Fact]
    public void FailedSettlementCalculationDoesNotPartiallyUpdateTheAggregate()
    {
        var invoice = SalesInvoice.Create(Guid.NewGuid(), new() { Lines = [SalesInvoiceCalculationTests.Line(Guid.NewGuid())] }, "actor", DateTimeOffset.UtcNow, new());
        var before = invoice.ToDraft(); var revision = invoice.Revision; var vat = invoice.VatAmount;
        var input = new InvoiceSettlementData { Charges = [new(FinancialMath.MaxAmount, "Powód")] };
        Assert.Contains(Assert.Throws<SalesInvoiceValidationException>(() => invoice.UpdateDraft(before with { Details = before.Details with { Settlement = input } }, "editor", DateTimeOffset.UtcNow, new())).Issues,
            x => x.Code == "SettlementArithmeticRange");
        Assert.Equal(revision, invoice.Revision); Assert.Null(invoice.ToDraft().Details.Settlement); Assert.Null(invoice.SettlementAmounts); Assert.Equal(vat, invoice.VatAmount);
    }

    [Fact]
    public void ChangesAreRecalculatedAndInputCollectionsAreDefensivelyCopied()
    {
        var entries = new[] { new InvoiceSettlementEntry(1m, "Powód") };
        var invoice = SalesInvoice.Create(Guid.NewGuid(), new() { Lines = [SalesInvoiceCalculationTests.Line(Guid.NewGuid())], Details = new() { Settlement = new() { Charges = entries } } }, "actor", DateTimeOffset.UtcNow, new());
        Assert.Equal(124m, invoice.SettlementAmounts!.AmountDue); entries[0] = new(1000m, "Zmieniony");
        Assert.Equal(1m, invoice.ToDraft().Details.Settlement!.Charges[0].Amount);
        var draft = invoice.ToDraft(); ((InvoiceSettlementEntry[])draft.Details.Settlement!.Charges)[0] = new(2m, "Zmiana");
        Assert.Equal(1m, invoice.ToDraft().Details.Settlement!.Charges[0].Amount);
        invoice.UpdateDraft(draft, "editor", DateTimeOffset.UtcNow, new());
        Assert.Equal(125m, invoice.SettlementAmounts!.AmountDue); Assert.Equal(23m, invoice.VatAmount);
        Assert.Throws<NotSupportedException>(() => ((IList<InvoiceSettlementEntry>)invoice.Details.Data.Settlement!.Charges).Clear());
    }

    [Theory]
    [InlineData("PLN")] [InlineData("EUR")]
    public void SettlementUsesInvoiceCurrencyAndNeverChangesVatInPln(string currency)
    {
        var draft = new SalesInvoiceDraft
        {
            Currency = currency, Lines = [SalesInvoiceCalculationTests.Line(Guid.NewGuid())],
            ExchangeRates = currency == "PLN" ? [] : [new() { Id = Guid.NewGuid(), FromCurrency = "EUR", Rate = 4m, RateDate = new(2026, 11, 28) }]
        };
        var original = SalesInvoice.Create(Guid.NewGuid(), draft, "actor", DateTimeOffset.UtcNow, new());
        var invoice = SalesInvoice.Create(Guid.NewGuid(), draft with { Details = new() { Settlement = new() { Charges = [new(.01m, "Koszty")] } } }, "actor", DateTimeOffset.UtcNow, new());
        Assert.Equal(123.01m, invoice.SettlementAmounts!.AmountDue);
        Assert.Equal(original.NetAmount, invoice.NetAmount); Assert.Equal(original.VatAmount, invoice.VatAmount);
        Assert.Equal(original.GrossAmount, invoice.GrossAmount); Assert.Equal(original.VatAmountPln, invoice.VatAmountPln);
    }
}
