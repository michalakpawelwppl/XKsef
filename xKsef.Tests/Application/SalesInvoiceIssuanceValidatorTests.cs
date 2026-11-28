using System.Text.Json;
using xKsef.Application.SalesInvoices;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Tests.Application;

public sealed class SalesInvoiceIssuanceValidatorTests
{
    private readonly SalesInvoiceCalculator _calculator = new();
    private SalesInvoiceIssuanceValidator Validator => new(_calculator);
    private static readonly DateOnly IssueDate = new(2026, 11, 27);
    private static SalesInvoiceDraft CompleteDraft() => new()
    {
        PlannedIssueDate = IssueDate, SaleDate = IssueDate,
        SaleDateMode = SaleDateMode.CommonDate, Fa3 = new() { Qualifications = SalesInvoiceSnapshotTestData.ConfirmedQualifications() },
        Seller = new() { Name = "Sprzedawca", Identifier = "8567346215", Address = new("PL", "Warszawa, ul. Testowa 1") },
        Buyer = new() { Name = "Nabywca", Identifier = "8567346215", Address = new("PL", "Kraków, ul. Testowa 2") },
        Lines = [new() { Id = Guid.NewGuid(), Description = "Usługa", Unit = "szt.", Quantity = 2m, UnitPrice = 100m }]
    };
    private SalesInvoice Invoice(SalesInvoiceDraft? draft = null) => SalesInvoice.Create(Guid.NewGuid(), draft ?? CompleteDraft(), "user", DateTimeOffset.UtcNow, _calculator);
    private static void Set(object target, string property, object? value) => target.GetType().GetProperty(property)!.SetValue(target, value);

    [Theory]
    [InlineData(VatCalculationMethod.PerLineNet)]
    [InlineData(VatCalculationMethod.PerLineGross)]
    [InlineData(VatCalculationMethod.ByNetTotals)]
    [InlineData(VatCalculationMethod.ByGrossTotals)]
    public void CompleteInvoiceIsReadyWithoutNumberOrPaymentDetails(VatCalculationMethod method)
    {
        var result = Validator.Validate(Invoice(CompleteDraft() with { VatMethod = method }));
        Assert.True(result.IsReadyForIssuance); Assert.Empty(result.Errors); Assert.Empty(result.Warnings);
    }

    [Fact]
    public void MissingBuyerAndSellerFieldsAreAllReported()
    {
        var result = Validator.Validate(Invoice(CompleteDraft() with { Seller = new(), Buyer = null }));
        Assert.False(result.IsReadyForIssuance);
        Assert.Contains(result.Errors, x => x.Path == "seller.name" && x.Code == "Required");
        Assert.Contains(result.Errors, x => x.Path == "seller.identifier");
        Assert.Contains(result.Errors, x => x.Path == "seller.address" && x.Code == "AddressRequired");
        Assert.Contains(result.Errors, x => x.Path == "buyer" && x.Code == "PartyRequired");
    }

    [Fact]
    public void EmptyDraftCanStillBeSavedButIsNotReady()
    {
        var invoice = Invoice(new());
        var result = Validator.Validate(invoice);
        Assert.False(result.IsReadyForIssuance);
        Assert.Contains(result.Errors, x => x.Code == "LinesRequired");
        Assert.Contains(result.Errors, x => x.Code == "IssueDateRequired");
        Assert.Contains(result.Errors, x => x.Code == "CalculationIncomplete");
        Assert.Contains(result.Errors, x => x.Path == "seller");
    }

    [Fact]
    public void IncompleteLineHasFieldPathsContainingItsStableId()
    {
        var id = Guid.NewGuid();
        var result = Validator.Validate(Invoice(CompleteDraft() with { Lines = [new() { Id = id }] }));
        foreach (var field in new[] { "description", "unit", "quantity", "unitPrice" }) Assert.Contains(result.Errors, x => x.Path == $"lines[{id}].{field}");
    }

    [Theory]
    [InlineData("99")]
    [InlineData("0")]
    [InlineData("")]
    public void InvalidVatQualificationUsesExistingValidation(string code)
    {
        var invoice = Invoice(); Set(invoice.Lines[0], "TaxCode", code);
        var result = Validator.Validate(invoice);
        Assert.False(result.IsReadyForIssuance);
        Assert.Contains(result.Errors, x => x.Path == $"lines[{invoice.Lines[0].Id}].taxCode");
    }

    [Fact]
    public void ExemptionCannotBeReadyWithoutRepresentableBasis()
    {
        var draft = CompleteDraft();
        var result = Validator.Validate(Invoice(draft with { Lines = [draft.Lines[0] with { TaxCode = "zw" }] }));
        Assert.Contains(result.Errors, x => x.Code == "ExemptionBasisNotRepresented");
    }

    [Theory]
    [InlineData("23")][InlineData("22")][InlineData("8")][InlineData("7")][InlineData("5")][InlineData("4")][InlineData("3")]
    [InlineData("0 KR")][InlineData("0 WDT")][InlineData("0 EX")][InlineData("np I")][InlineData("np II")][InlineData("oo")]
    public void OtherSchemaVatCodesAreNotArbitrarilyRestricted(string code)
    {
        var draft = CompleteDraft();
        if (code == "oo") draft = draft with { Fa3 = draft.Fa3 with { Qualifications = draft.Fa3.Qualifications with { ReverseCharge = new() { Value = QualificationValue.Yes, Source = QualificationSource.UserConfirmed } } } };
        Assert.True(Validator.Validate(Invoice(draft with { Lines = [draft.Lines[0] with { TaxCode = code }] })).IsReadyForIssuance);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(-10)]
    [InlineData(10)]
    public void SaleDateMayBeAbsentEqualEarlierOrLaterThanIssueDate(int? offset)
    {
        var result = Validator.Validate(Invoice(CompleteDraft() with { SaleDate = offset is null ? null : IssueDate.AddDays(offset.Value), SaleDateMode = offset is null ? SaleDateMode.NotDetermined : SaleDateMode.CommonDate }));
        Assert.True(result.IsReadyForIssuance);
        Assert.Equal(offset is null, result.Warnings.Any(x => x.Code == "SaleDateUnspecified"));
    }

    [Fact]
    public void AbsentBuyerIdAndAddressAreSupportedSchemaVariants()
    {
        var result = Validator.Validate(Invoice(CompleteDraft() with { Buyer = new() { Name = "Konsument", IdentifierKind = TaxIdentifierKind.None } }));
        Assert.True(result.IsReadyForIssuance);
        Assert.Contains(result.Warnings, x => x.Code == "BuyerAddressMissing");
    }

    [Theory]
    [InlineData(TaxIdentifierKind.EuVat, "123456789", "DE", true)]
    [InlineData(TaxIdentifierKind.EuVat, "123456789", "ZZ", false)]
    [InlineData(TaxIdentifierKind.Other, "ABC", null, true)]
    [InlineData(TaxIdentifierKind.Other, "ABC", "ZZ", false)]
    [InlineData(TaxIdentifierKind.Nip, "1234567890", null, false)]
    [InlineData(TaxIdentifierKind.None, "123", null, false)]
    public void BuyerIdentifierVariantsAreValidated(TaxIdentifierKind kind, string id, string? country, bool ready)
    {
        var draft = CompleteDraft();
        var result = Validator.Validate(Invoice(draft with { Buyer = draft.Buyer! with { IdentifierKind = kind, Identifier = id, IdentifierCountryCode = country } }));
        Assert.Equal(ready, result.IsReadyForIssuance);
    }

    [Fact]
    public void CurrencyRequiresExistingModelExchangeRateAndCalculatedVatInPln()
    {
        var draft = CompleteDraft() with { Currency = "EUR" };
        var missing = Validator.Validate(Invoice(draft));
        Assert.False(missing.IsReadyForIssuance); Assert.Contains(missing.Errors, x => x.Path == "exchangeRates");
        var ready = Validator.Validate(Invoice(draft with { ExchangeRates = [new() { Id = Guid.NewGuid(), Rate = 4.25m, RateDate = IssueDate }] }));
        Assert.True(ready.IsReadyForIssuance);
    }

    [Theory]
    [InlineData("NetAmount", null)]
    [InlineData("VatAmount", "1")]
    [InlineData("GrossAmount", "1")]
    [InlineData("VatAmountPln", null)]
    public void MissingOrChangedPersistedTotalsAreRejected(string property, string? value)
    {
        var invoice = Invoice(); Set(invoice, property, value is null ? null : decimal.Parse(value));
        Assert.False(Validator.Validate(invoice).IsReadyForIssuance);
    }

    [Fact]
    public void TamperedLineAndTaxSummaryAreReportedIndependently()
    {
        var invoice = Invoice(); Set(invoice.Lines[0], "NetAmount", 10m); Set(invoice.TaxSummaries[0], "VatAmount", 10m);
        var result = Validator.Validate(invoice);
        Assert.Contains(result.Errors, x => x.Code == "LineAmountsMismatch");
        Assert.Contains(result.Errors, x => x.Code == "TaxSummariesMismatch");
    }

    [Fact]
    public void OutdatedCalculationVersionRequiresRecalculation()
    {
        var invoice = Invoice(); Set(invoice, "CalculationVersion", "old");
        Assert.Contains(Validator.Validate(invoice).Errors, x => x.Code == "CalculationVersionOutdated");
    }

    [Fact]
    public void InvalidPersistedDatesAndCurrencyAreReportedWithoutThrowing()
    {
        var invoice = Invoice(); Set(invoice, "PlannedIssueDate", new DateOnly(2051, 1, 1)); Set(invoice, "Currency", "BAD");
        var result = Validator.Validate(invoice);
        Assert.Contains(result.Errors, x => x.Path == "date"); Assert.Contains(result.Errors, x => x.Path == "currency");
    }

    [Fact]
    public void RelativePaymentDescriptionMustBeCompleteButPaymentIsOptional()
    {
        var result = Validator.Validate(Invoice(CompleteDraft() with { Details = new() { PaymentTerms = [new(null, 14)] } }));
        Assert.Contains(result.Errors, x => x.Code == "PaymentTermDescriptionIncomplete");
        Assert.True(Validator.Validate(Invoice(CompleteDraft() with { Details = new() { PaymentTerms = [new(null, 14, "dni", "data wystawienia")] } })).IsReadyForIssuance);
    }

    [Fact]
    public void ValidationDoesNotModifyAnyInvoiceDataAndIsRepeatable()
    {
        var invoice = Invoice(); var before = JsonSerializer.Serialize(invoice);
        var one = Validator.Validate(invoice); var two = Validator.Validate(invoice);
        Assert.Equal(before, JsonSerializer.Serialize(invoice));
        Assert.Equal(one.Errors, two.Errors); Assert.Equal(one.Warnings, two.Warnings);
    }

    [Fact]
    public void InvalidVatDoesNotHideOtherMissingFinancialFields()
    {
        var draft = CompleteDraft();
        var invoice = Invoice(draft with { Lines = [draft.Lines[0] with { Quantity = null, UnitPrice = null }] });
        Set(invoice.Lines[0], "TaxCode", "BAD");
        var result = Validator.Validate(invoice);
        Assert.Contains(result.Errors, x => x.Code == "InvalidTaxCode");
        Assert.Contains(result.Errors, x => x.Path.EndsWith(".quantity", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.Path.EndsWith(".unitPrice", StringComparison.Ordinal));
        Assert.Contains(result.Errors, x => x.Code == "CalculationIncomplete");
    }

    [Fact]
    public void InvalidXmlCharactersAreReportedEvenInOptionalFields()
    {
        var draft = CompleteDraft();
        var result = Validator.Validate(Invoice(draft with { Notes = "Uwagi\u0001", Lines = [draft.Lines[0] with { Description = "Usługa\u0002" }] }));
        Assert.Contains(result.Errors, x => x.Path == "notes" && x.Code == "InvalidXmlCharacters");
        Assert.Contains(result.Errors, x => x.Path.EndsWith(".description", StringComparison.Ordinal) && x.Code == "InvalidXmlCharacters");
    }

    [Theory]
    [InlineData("ZZ", false)]
    [InlineData("PL", true)]
    [InlineData("XI", true)]
    public void AddressCountryUsesLocalSchemaVocabularyWithoutInventedGlnPattern(string country, bool ready)
    {
        var draft = CompleteDraft();
        var result = Validator.Validate(Invoice(draft with { Seller = draft.Seller! with { Address = new(country, "Adres", Gln: "ABC") } }));
        Assert.Equal(ready, result.IsReadyForIssuance);
    }

    [Fact]
    public void CurrencyMixedRatesAndDiscountsKeepConsistentPersistedSummaries()
    {
        var draft = CompleteDraft();
        var invoice = Invoice(draft with
        {
            Currency = "JPY", GlobalDiscount = new(DiscountKind.Percent, 10m), VatMethod = VatCalculationMethod.ByGrossTotals,
            ExchangeRates = [new() { Id = Guid.NewGuid(), FromCurrency = "JPY", Rate = 2.65m, QuotedUnits = 100m, RateDate = IssueDate }],
            Lines = [draft.Lines[0], draft.Lines[0] with { Id = Guid.NewGuid(), PriceBasis = PriceBasis.Gross, TaxCode = "8" }]
        });
        Assert.True(Validator.Validate(invoice).IsReadyForIssuance);
    }
}
