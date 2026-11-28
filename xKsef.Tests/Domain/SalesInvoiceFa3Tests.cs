using System.Text.Json;
using xKsef.Application.SalesInvoices;
using xKsef.Application.SalesInvoices.Snapshots;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Tests.Domain;

public sealed class SalesInvoiceFa3Tests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 10, 0, 0, TimeSpan.Zero);
    private static SalesInvoiceDraft Draft() => new()
    {
        PlannedIssueDate = new(2026, 11, 28), Seller = new() { Name = "Sprzedawca", Identifier = "8567346215", Address = new("PL", "Adres") },
        Buyer = new() { Name = "Nabywca", IdentifierKind = TaxIdentifierKind.None },
        Lines = [new() { Id = Guid.NewGuid(), Description = "Towar", Unit = "szt.", Quantity = 1, UnitPrice = 100 }]
    };
    private static SalesInvoice Create(SalesInvoiceDraft draft) => SalesInvoice.Create(Guid.NewGuid(), draft, "server-user", Now, new());

    [Theory]
    [InlineData(VatCalculationMethod.PerLineNet)]
    [InlineData(VatCalculationMethod.PerLineGross)]
    public void PositionalMethodsAreCalculatedAndValidated(VatCalculationMethod method)
    {
        var invoice = Create(Draft() with { VatMethod = method, SaleDateMode = SaleDateMode.SameAsIssueDate, Fa3 = new() { Qualifications = SalesInvoiceSnapshotTestData.ConfirmedQualifications() } });
        Assert.True(invoice.CalculationComplete); Assert.Equal(100m, invoice.NetAmount); Assert.Equal(23m, invoice.VatAmount); Assert.Equal(123m, invoice.GrossAmount);
        var result = new SalesInvoiceIssuanceValidator(new()).Validate(invoice);
        Assert.True(result.IsReadyForIssuance); Assert.Equal(method, invoice.ToDraft().VatMethod);
    }
    [Fact]
    public void ExactlyFourMethodsHaveTheRequiredWireValues()
    {
        Assert.Equal(0, (int)VatCalculationMethod.PerLineNet); Assert.Equal(1, (int)VatCalculationMethod.PerLineGross); Assert.Equal(2, (int)VatCalculationMethod.ByNetTotals); Assert.Equal(3, (int)VatCalculationMethod.ByGrossTotals); Assert.Equal(4, Enum.GetValues<VatCalculationMethod>().Length);
        var draft = Draft(); var line = draft.Lines[0];
        var invoice = Create(draft with { Lines = [line, line with { Id = Guid.NewGuid(), UnitPrice = 123, PriceBasis = PriceBasis.Gross }] });
        Assert.Equal(200m, invoice.NetAmount); Assert.Equal(46m, invoice.VatAmount); Assert.Equal(246m, invoice.GrossAmount);
    }

    [Fact]
    public void ExplicitCompatibleDomesticTreatmentKeepsExistingAmountsButConflictingTreatmentDoesNot()
    {
        var draft = Draft(); var legacy = Create(draft);
        var current = Create(draft with { Lines = [draft.Lines[0] with { TaxTreatment = new() { Kind = InvoiceTaxTreatmentKind.DomesticTaxable, DomesticRateCode = "23" } }] });
        Assert.True(current.CalculationComplete); Assert.Equal(legacy.NetAmount, current.NetAmount); Assert.Equal(legacy.VatAmount, current.VatAmount); Assert.Equal(legacy.GrossAmount, current.GrossAmount);
        var conflicting = Create(draft with { Lines = [draft.Lines[0] with { TaxTreatment = new() { Kind = InvoiceTaxTreatmentKind.DomesticTaxable, DomesticRateCode = "8" } }] });
        Assert.False(conflicting.CalculationComplete); Assert.Null(conflicting.VatAmount);
    }

    [Fact]
    public void AllNewDataRoundTripsAndInputCollectionsCannotMutateTheAggregate()
    {
        var numbers = new List<string> { "WZ/1", "WZ/2" }; var contacts = new List<InvoiceContact> { new("contact@example.com", null) };
        var draft = Draft();
        var input = draft with
        {
            IssuePlace = "Warszawa", SaleDateMode = SaleDateMode.Period, SalePeriod = new(null, new(2026, 11, 28)),
            Seller = draft.Seller! with { BuyerLinkKey = "KEY", CustomerNumber = "KL/1", SellerStatus = SellerInformationStatus.Restructuring, TaxpayerStatus = QualificationValue.Yes, PlPrefix = new() { Value = QualificationValue.No, Source = QualificationSource.UserConfirmed } },
            Participants = [new() { Id = Guid.NewGuid(), Role = InvoiceParticipantRole.Other, SharePercent = 1.123456m, Party = new() { Contacts = contacts } }],
            Fa3 = new() { WarehouseDocumentNumbers = numbers, AuthorizedParty = new(AuthorizedPartyRole.TaxRepresentative, new() { Name = "Pełnomocnik", Contacts = contacts }) },
            Lines = [draft.Lines[0] with { SaleDate = new(2026, 11, 27), Pkob = "1110", Procedure = InvoiceProcedureCode.WSTO_EE, ExciseAmount = 2.50m, ExemptionBasis = new() { Kind = ExemptionBasisKind.PolishLaw }, Annex15 = new() { Value = QualificationValue.Yes }, TaxTreatment = new() { Kind = InvoiceTaxTreatmentKind.UnionOss, ForeignVatRate = 19.123456m, TaxCountryCode = "DE" } }]
        };
        var invoice = Create(input); var before = JsonSerializer.Serialize(invoice.ToDraft());
        numbers.Clear(); contacts.Clear();
        Assert.Equal(before, JsonSerializer.Serialize(invoice.ToDraft()));
        var output = invoice.ToDraft(); Assert.Equal(2, output.ModelVersion); Assert.Equal("Warszawa", output.IssuePlace);
        Assert.Null(output.SalePeriod!.From); Assert.Equal(1.123456m, output.Participants[0].SharePercent);
        Assert.Equal(19.123456m, output.Lines[0].TaxTreatment!.ForeignVatRate);
        Assert.Null(output.Lines[0].ExemptionBasis!.Text);
        Assert.False(invoice.CalculationComplete); Assert.Null(invoice.VatAmount);
    }

    [Theory]
    [InlineData(QualificationValue.Unknown)]
    [InlineData(QualificationValue.No)]
    [InlineData(QualificationValue.Yes)]
    public void QualificationValuesAreDistinctAndAuditComesFromTheServer(QualificationValue value)
    {
        var decision = new QualificationDecisionData { Value = value, Source = value == QualificationValue.Unknown ? QualificationSource.Unresolved : QualificationSource.UserConfirmed,
            SuggestedValue = QualificationValue.Yes, ConfirmedBy = "forged", ConfirmedAt = Now.AddYears(-1), ProposedBy = "forged", ProposedAt = Now.AddYears(-1), RuleCode = "forged", RuleVersion = "forged", InputFingerprint = "forged" };
        var invoice = Create(Draft() with { Fa3 = new() { Qualifications = new() { SplitPayment = decision } } });
        var saved = invoice.Fa3.Qualifications.SplitPayment;
        Assert.Equal(value, saved.Value); Assert.Equal("server-user", saved.ProposedBy); Assert.Equal(Now, saved.ProposedAt);
        Assert.Null(saved.RuleCode); Assert.Null(saved.RuleVersion); Assert.Null(saved.InputFingerprint);
        if (value == QualificationValue.Unknown) { Assert.Null(saved.ConfirmedBy); Assert.Null(saved.ConfirmedAt); }
        else { Assert.Equal("server-user", saved.ConfirmedBy); Assert.Equal(Now, saved.ConfirmedAt); }
        Assert.False(new SalesInvoiceIssuanceValidator(new()).Validate(invoice).IsReadyForIssuance);
    }

    [Fact]
    public void ChangingSourceDataInvalidatesConfirmationsAndPreservesTheirProposals()
    {
        var invoice = Create(Draft() with { Fa3 = new() { Qualifications = new() { CashAccounting = new() { Value = QualificationValue.No, Source = QualificationSource.UserConfirmed } } } });
        invoice.UpdateDraft(invoice.ToDraft(), "other-user", Now.AddMinutes(1), new());
        Assert.Equal("server-user", invoice.Fa3.Qualifications.CashAccounting.ConfirmedBy);
        invoice.UpdateDraft(invoice.ToDraft() with { IssuePlace = "Nowe miejsce" }, "editor", Now.AddMinutes(2), new());
        Assert.Equal(QualificationValue.No, invoice.Fa3.Qualifications.CashAccounting.Value);
        Assert.Equal(QualificationSource.UserProposed, invoice.Fa3.Qualifications.CashAccounting.Source);
        Assert.Null(invoice.Fa3.Qualifications.CashAccounting.ConfirmedBy);
    }

    [Fact]
    public void IncompleteFa3DraftIsSavableButCannotBeApproved()
    {
        var invoice = Create(new() { ModelVersion = 2, SaleDateMode = SaleDateMode.Unknown, SalePeriod = new(null, null),
            Participants = [new() { Id = Guid.NewGuid() }], Lines = [new() { Id = Guid.NewGuid(), TaxTreatment = new(), ExemptionBasis = new() }] });
        Assert.Equal(1, invoice.Revision); Assert.Null(invoice.GrossAmount);
        Assert.Contains(new SalesInvoiceIssuanceValidator(new()).Validate(invoice).Errors, x => x.Code == "QualificationUnresolved");
        Assert.Throws<SalesInvoiceValidationException>(() => invoice.Approve(invoice.Revision, "user", Now, "{}"));
        Assert.Equal(SalesInvoiceStatus.Draft, invoice.Status); Assert.Empty(invoice.Approvals);
    }

    [Fact]
    public void InvalidStructuresDoNotMutateTheExistingAggregate()
    {
        var invoice = Create(Draft()); var before = JsonSerializer.Serialize(invoice);
        var invalid = invoice.ToDraft() with { Fa3 = new() { Version = 999 } };
        Assert.Throws<SalesInvoiceValidationException>(() => invoice.UpdateDraft(invalid, "user", Now, new()));
        Assert.Equal(before, JsonSerializer.Serialize(invoice));
        Assert.Throws<SalesInvoiceValidationException>(() => invoice.UpdateDraft(invoice.ToDraft() with { Fa3 = null! }, "user", Now, new()));
        Assert.Equal(before, JsonSerializer.Serialize(invoice));
    }
}
