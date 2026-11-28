
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using xKsef.Application.SalesInvoices;
using xKsef.Application.SalesInvoices.Snapshots;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Tests.Application;

public sealed class SalesInvoiceSnapshotTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 28, 12, 0, 0, TimeSpan.Zero);
    private static ISalesInvoiceApprovalSnapshotReader Reader => new SalesInvoiceApprovalSnapshotReader();
    private static SalesInvoice Create(VatCalculationMethod method = VatCalculationMethod.ByNetTotals, SalesInvoiceDetailsData? details = null)
    {
        var a = Guid.Parse("00000000-0000-0000-0000-000000000003"); var b = Guid.Parse("00000000-0000-0000-0000-000000000001");
        var party = new SalesInvoicePartyData { Name = "Nabywca", IdentifierKind = TaxIdentifierKind.None, SourceContractorId = Guid.NewGuid(), BuyerLinkKey = "KEY", CustomerNumber = "KL-1",
            TaxpayerStatus = QualificationValue.Yes, PlPrefix = new() { Value = QualificationValue.No, Source = QualificationSource.UserConfirmed },
            Address = new("PL", "Adres", "Adres 2", "123"), CorrespondenceAddress = new("PL", "Korespondencja"), Contacts = [new("buyer@example.com", "123456789")] };
        var invoice = SalesInvoice.Create(Guid.NewGuid(), new()
        {
            NumberSeriesId = Guid.NewGuid(), PlannedIssueDate = new(2026, 11, 28), IssuePlace = "Warszawa", SaleDateMode = SaleDateMode.Period,
            SalePeriod = new(new(2026, 11, 1), new(2026, 11, 28)), Currency = "EUR", VatMethod = method, PdfLanguage = PdfPresentationLanguage.PlEn,
            Seller = new() { Name = "Sprzedawca", Identifier = "8567346215", Address = new("PL", "Adres"), SellerStatus = SellerInformationStatus.Restructuring }, Buyer = party,
            Participants = [new() { Id = Guid.NewGuid(), Role = InvoiceParticipantRole.AdditionalBuyer, SharePercent = 40.123456m, Party = party with { Name = "Uczestnik" } }],
            Fa3 = new() { WarehouseDocumentNumbers = ["WZ/2", "WZ/1"], Qualifications = SalesInvoiceSnapshotTestData.ConfirmedQualifications(),
                AuthorizedParty = new(AuthorizedPartyRole.TaxRepresentative, new() { Name = "Przedstawiciel", Identifier = "8567346215", Address = new("PL", "Adres PU") }) },
            Lines = [new() { Id = a, Description = "Towar", Unit = "szt.", Quantity = 2.345678m, UnitPrice = 12.34567891m, PriceBasis = PriceBasis.Gross, TaxCode = "23",
                Discount = new(DiscountKind.Percent, 12.5m), TaxTreatment = new() { Kind = InvoiceTaxTreatmentKind.DomesticTaxable, DomesticRateCode = "23", TaxCountryCode = "PL" },
                ProductCode = "KOD", Gtin = "12345", Pkwiu = "01", Cn = "02", Gtu = "GTU_01", Pkob = "1110", ExciseAmount = 2.50m,
                Annex15 = new() { Value = QualificationValue.No, Source = QualificationSource.UserConfirmed } },
                new() { Id = b, Description = "Druga pozycja", Unit = "szt.", Quantity = 1, UnitPrice = .008m }],
            ExchangeRates = [new() { Id = Guid.NewGuid(), FromCurrency = "EUR", Rate = 4.123456789012m, QuotedUnits = 1, Source = ExchangeRateSource.Manual,
                SourceReference = "Tabela", RateDate = new(2026, 11, 27), ReferenceDate = new(2026, 11, 28), SelectionRule = "Ręczny" }],
            GlobalDiscount = new(DiscountKind.Percent, 2), GlobalDiscountBasis = PriceBasis.Gross, Notes = "Żółć & <opis>",
            Details = details ?? new() { PaymentMethod = 6, PaymentTerms = [new(new(2026, 12, 15))], BankAccounts = [new("12345678901234567890123456", "ABCDEFGH", "Bank", "Opis")], AdditionalDescriptions = [new("Klucz", "Wartość", a)] }
        }, "creator", Now, new());
        typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.RowVersion))!.SetValue(invoice, new byte[8]);
        return invoice;
    }
    private static SalesInvoiceApproval Approve(SalesInvoice invoice)
    {
        var id = Guid.NewGuid(); var writer = new SalesInvoiceApprovalSnapshotWriter();
        var json = writer.Write(invoice, id, "approver", Now);
        Assert.Equal(json, writer.Write(invoice, id, "approver", Now));
        invoice.Approve(invoice.Revision, "approver", Now, json, id);
        return Assert.Single(invoice.Approvals);
    }

    [Theory]
    [InlineData(VatCalculationMethod.PerLineNet)]
    [InlineData(VatCalculationMethod.PerLineGross)]
    [InlineData(VatCalculationMethod.ByNetTotals)]
    [InlineData(VatCalculationMethod.ByGrossTotals)]
    public void FullSnapshotRoundTripPreservesCurrentModelOrderPrecisionAuditAndSalesV3(VatCalculationMethod method)
    {
        var invoice = Create(method); Assert.True(new SalesInvoiceIssuanceValidator(new()).Validate(invoice).IsReadyForIssuance);
        var approval = Approve(invoice); var before = JsonSerializer.Serialize(approval); var doc = Reader.Read(approval);
        Assert.Equal(2, doc.ModelVersion); Assert.Equal("sales-v3", doc.CalculatorVersion);
        Assert.Equal("issuance-v2", doc.RulesVersion); Assert.Equal("fa3-model-v1", doc.ProjectionVersion);
        Assert.Equal(invoice.ToDraft().Lines.Select(x => x.Id), doc.Input!.Lines!.Select(x => x.Id!.Value));
        Assert.Equal(new int?[] { 1, 2 }, doc.Input.Lines!.Select(x => x.Ordinal));
        Assert.Equal(12.34567891m, doc.Input.Lines![0].UnitPrice!.Value.Value); Assert.Equal(2.345678m, doc.Input.Lines[0].Quantity!.Value.Value);
        Assert.Equal(12.5m, doc.Input.Lines[0].Discount!.Value!.Value.Value); Assert.Equal("PL", doc.Input.Lines[0].TaxTreatment!.TaxCountryCode);
        Assert.Equal("creator", doc.Input.Lines[0].Annex15!.ConfirmedBy); Assert.Equal(2.50m, doc.Input.Lines[0].ExciseAmount!.Value.Value);
        Assert.Equal("creator", doc.Input.Fa3!.Qualifications!.CashAccounting!.ConfirmedBy);
        Assert.Equal("KEY", doc.Input.Buyer!.BuyerLinkKey); Assert.Equal("KL-1", doc.Input.Buyer.CustomerNumber);
        Assert.Equal(40.123456m, Assert.Single(doc.Input.Participants!).SharePercent!.Value.Value);
        Assert.Equal("Przedstawiciel", doc.Input.Fa3.AuthorizedParty!.Party!.Name); Assert.Equal(new[] { "WZ/2", "WZ/1" }, doc.Input.Fa3.WarehouseDocumentNumbers);
        Assert.Equal(new DateOnly(2026, 11, 1), doc.Input.SalePeriod!.From); Assert.Equal(4.123456789012m, Assert.Single(doc.Input.ExchangeRates!).Rate!.Value.Value);
        Assert.Equal("Klucz", Assert.Single(doc.Input.Details!.AdditionalDescriptions!).Key);
        Assert.Equal(invoice.NetAmount, doc.Calculations!.Totals!.Net!.Value.Value); Assert.Equal(invoice.VatAmount, doc.Calculations.Totals.Vat!.Value.Value);
        foreach (var line in invoice.Lines)
        {
            var captured = doc.Calculations.Lines!.Single(x => x.Id == line.Id);
            Assert.Equal(line.VatAmount, captured.Vat!.Value.Value); Assert.Equal((int?)line.VatAmountOrigin, captured.VatAmountOrigin);
            Assert.Equal(line.VatRoundingAdjustment, captured.VatRoundingAdjustment!.Value.Value); Assert.Equal(line.VatBaseRoundingAdjustment, captured.VatBaseRoundingAdjustment!.Value.Value);
            Assert.Equal(new SalesInvoiceCalculator().Calculate(invoice.ToDraft()).Lines.Single(x => x.Id == line.Id).LineDiscount, captured.LineDiscount!.Value.Value);
        }
        Assert.All(doc.Calculations.TaxSummaries!, x => Assert.Equal((int)method, x.CalculationDetails!.Method));
        Assert.True(ApprovedSalesInvoiceComparison.Matches(invoice, doc)); Assert.Equal(before, JsonSerializer.Serialize(approval));
        var projectedVat = doc.Disclosure!.Fields!.Single(x => x.Path == "Lines[1].Vat");
        Assert.Equal(method is VatCalculationMethod.ByNetTotals or VatCalculationMethod.ByGrossTotals ? DisclosureState.NotDisclosed : DisclosureState.Present, projectedVat.State);
        var projected = JsonSerializer.Serialize(doc.Disclosure.Fields);
        Assert.DoesNotContain("\"SourceContractorId\":", projected);
        Assert.DoesNotContain("\"LineId\":", projected);
        Assert.DoesNotContain("ConfirmedBy", projected); Assert.DoesNotContain("\"CalculationDetails\":", projected);
        Assert.Equal(DisclosureState.Unresolved, doc.Disclosure.Fields!.Single(x => x.Path == "Seller.PlPrefix").State);
        Assert.Equal(1, doc.Disclosure.Fields!.Single(x => x.Path == "Details").Value!.Value.GetProperty("AdditionalDescriptions")[0].GetProperty("LineOrdinal").GetInt32());
        Assert.Equal(DisclosureState.FromNumberReservation, doc.Disclosure.Fields!.Single(x => x.Path == "InvoiceNumber").State);
        Assert.Throws<NotSupportedException>(() => ((IList<ApprovedSalesInvoiceLine>)doc.Input.Lines).Clear());
        Assert.Throws<NotSupportedException>(() => ((IList<ApprovedDisclosureField>)doc.Disclosure.Fields!).Clear());
    }

    [Fact]
    public void ReadDoesNotDependOnChangedDraftPartyOrCalculatorAndDoesNotRecalculate()
    {
        var invoice = Create(); var approval = Approve(invoice); var before = JsonSerializer.Serialize(Reader.Read(approval));
        Assert.Throws<SalesInvoiceConflictException>(() => invoice.UpdateDraft(invoice.ToDraft() with { Notes = "Zmiana" }, "editor", Now, new()));
        typeof(SalesInvoiceLine).GetProperty(nameof(SalesInvoiceLine.UnitPrice))!.SetValue(invoice.Lines[0], 999m);
        typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.CalculationVersion))!.SetValue(invoice, "future-calculator");
        var party = invoice.Parties.Single(x => x.Role == PartyRole.Buyer);
        typeof(SalesInvoicePartySnapshot).GetProperty(nameof(SalesInvoicePartySnapshot.Data))!.SetValue(party, party.Data with { Name = "Zmieniona kartoteka" });
        Assert.Equal(before, JsonSerializer.Serialize(Reader.Read(approval)));
        Assert.False(ApprovedSalesInvoiceComparison.Matches(invoice, Reader.Read(approval)));
    }

    [Fact]
    public void MissingOptionalPropertiesRemainUnknownAndDoNotRequireAnotherFormat()
    {
        var row = Approve(Create()); var json = JsonNode.Parse(row.SnapshotJson)!;
        json["Input"]!.AsObject().Remove("Notes");
        json["Input"]!["Buyer"]!.AsObject().Remove("TaxpayerStatus");
        json["Input"]!["Lines"]![0]!.AsObject().Remove("ExciseAmount");
        json["Input"]!["Fa3"]!["Qualifications"]!.AsObject().Remove("Article42Paragraph5Applies");
        typeof(SalesInvoiceApproval).GetProperty(nameof(SalesInvoiceApproval.SnapshotJson))!.SetValue(row, json.ToJsonString());
        var snapshot = Reader.Read(row);
        Assert.Null(snapshot.Input!.Notes); Assert.Null(snapshot.Input.Buyer!.TaxpayerStatus); Assert.Null(snapshot.Input.Lines![0].ExciseAmount);
        Assert.Null(snapshot.Input.Fa3!.Qualifications!.Article42Paragraph5Applies);
        Assert.Equal(12.34567891m, snapshot.Input.Lines[0].UnitPrice!.Value.Value);
    }

    [Fact]
    public void SettlementSnapshotFreezesInputAndResultsWithoutReadingOrRecalculatingCurrentData()
    {
        var invoice = Create(details: new() { Settlement = new() { Charges = [new(17m, "Zwrot kosztów")], Deductions = [new(300m, "Saldo klienta")] } });
        var approval = Approve(invoice); var original = approval.SnapshotJson; var snapshot = Reader.Read(approval);
        Assert.Equal("Zwrot kosztów", Assert.Single(snapshot.Input!.Details!.Settlement!.Charges!).Reason);
        Assert.Equal(17m, snapshot.Calculations!.SettlementAmounts!.ChargesTotal!.Value.Value);
        Assert.Equal(300m - invoice.GrossAmount - 17m, snapshot.Calculations.SettlementAmounts.Overpayment!.Value.Value);
        Assert.True(ApprovedSalesInvoiceComparison.Matches(invoice, snapshot));
        Assert.Throws<NotSupportedException>(() => ((IList<ApprovedInvoiceSettlementEntry>)snapshot.Input.Details.Settlement.Charges!).Clear());
        var disclosed = snapshot.Disclosure!.Fields!.Single(x => x.Path == "SettlementAmounts").Value!.Value;
        Assert.Equal("17", disclosed.GetProperty("ChargesTotal").GetString());
        typeof(SalesInvoice).GetProperty(nameof(SalesInvoice.SettlementAmounts))!.SetValue(invoice, new InvoiceSettlementAmounts(1, 0, 1, null));
        Assert.False(ApprovedSalesInvoiceComparison.Matches(invoice, snapshot));
        Assert.Equal(original, approval.SnapshotJson);
        Assert.Equal(17m, Reader.Read(approval).Calculations!.SettlementAmounts!.ChargesTotal!.Value.Value);
    }

    [Fact]
    public void HistoricalSnapshotWithoutSettlementReadsNullAndDoesNotRequireReapproval()
    {
        var invoice = Create(); var row = Approve(invoice); var json = JsonNode.Parse(row.SnapshotJson)!;
        json["Input"]!["Details"]!.AsObject().Remove("Settlement"); json["Calculations"]!.AsObject().Remove("SettlementAmounts");
        typeof(SalesInvoiceApproval).GetProperty(nameof(SalesInvoiceApproval.SnapshotJson))!.SetValue(row, json.ToJsonString());
        var original = row.SnapshotJson; var snapshot = Reader.Read(row);
        Assert.Null(snapshot.Input!.Details!.Settlement); Assert.Null(snapshot.Calculations!.SettlementAmounts);
        Assert.True(ApprovedSalesInvoiceComparison.Matches(invoice, snapshot)); Assert.Equal(original, row.SnapshotJson);
    }

    [Fact]
    public void IncorrectFrozenSettlementAmountsAreRejectedInsteadOfRecalculated()
    {
        var invoice = Create(details: new() { Settlement = new() { Charges = [new(1m, "Powód")] } }); var row = Approve(invoice);
        var json = JsonNode.Parse(row.SnapshotJson)!; json["Calculations"]!["SettlementAmounts"]!["ChargesTotal"] = "2";
        typeof(SalesInvoiceApproval).GetProperty(nameof(SalesInvoiceApproval.SnapshotJson))!.SetValue(row, json.ToJsonString());
        Assert.Equal("SnapshotIncomplete", Assert.Throws<SalesInvoiceApprovalSnapshotException>(() => Reader.Read(row)).Code);
    }

    [Fact]
    public void BankAccountSnapshotKeepsBothCollectionsTypesAndOrderWithoutDependingOnLaterData()
    {
        var seller = new InvoiceBankAccount("seller/123", "WBKPPLPP", "Bank sprzedawcy", "Opis", BankOwnAccountType.PurchasedReceivables);
        var factor = new InvoiceBankAccount("factor/123", "ABCDEFGH123", "Bank faktora", "Faktoring", BankOwnAccountType.CollectionsForSupplier);
        var invoice = Create(details: new() { BankAccounts = [seller], FactorBankAccounts = [factor, new("other/1234", BankOwnAccountType: BankOwnAccountType.OwnOperationsNonSettlement)] });
        var draft = invoice.ToDraft();
        var validation = new SalesInvoiceIssuanceValidator(new()).Validate(invoice);
        Assert.True(validation.IsReadyForIssuance, JsonSerializer.Serialize(validation.Errors));
        var row = Approve(invoice); var original = row.SnapshotJson; var snapshot = Reader.Read(row);
        Assert.Equal(1, Assert.Single(snapshot.Input!.Details!.BankAccounts!).BankOwnAccountType);
        Assert.Equal(new int?[] { 2, 3 }, snapshot.Input.Details.FactorBankAccounts!.Select(x => x.BankOwnAccountType));
        Assert.Equal("Bank faktora", snapshot.Input.Details.FactorBankAccounts![0].BankName);
        Assert.Equal("ABCDEFGH123", snapshot.Input.Details.FactorBankAccounts![0].Swift);
        Assert.True(ApprovedSalesInvoiceComparison.Matches(invoice, snapshot));
        var projected = snapshot.Disclosure!.Fields!.Single(x => x.Path == "Details").Value!.Value;
        Assert.Equal(2, projected.GetProperty("FactorBankAccounts")[0].GetProperty("BankOwnAccountType").GetInt32());
        Assert.Throws<NotSupportedException>(() => ((IList<ApprovedBankAccount>)snapshot.Input.Details.FactorBankAccounts).Clear());
        typeof(SalesInvoiceDetails).GetProperty(nameof(SalesInvoiceDetails.Data))!.SetValue(invoice.Details, draft.Details with { FactorBankAccounts = [] });
        Assert.False(ApprovedSalesInvoiceComparison.Matches(invoice, snapshot));
        Assert.Equal("factor/123", Reader.Read(row).Input!.Details!.FactorBankAccounts![0].Number);
        Assert.Equal(original, row.SnapshotJson);
    }

    [Fact]
    public void HistoricalBankAccountJsonRemainsReadableAndComparableWithoutInventingMissingValues()
    {
        var invoice = Create(); var row = Approve(invoice); var json = JsonNode.Parse(row.SnapshotJson)!;
        var details = json["Input"]!["Details"]!;
        details.AsObject().Remove("FactorBankAccounts");
        foreach (var account in details["BankAccounts"]!.AsArray()) account!.AsObject().Remove("BankOwnAccountType");
        typeof(SalesInvoiceApproval).GetProperty(nameof(SalesInvoiceApproval.SnapshotJson))!.SetValue(row, json.ToJsonString());
        var before = row.SnapshotJson; var snapshot = Reader.Read(row);
        Assert.Null(snapshot.Input!.Details!.FactorBankAccounts);
        Assert.Null(Assert.Single(snapshot.Input.Details.BankAccounts!).BankOwnAccountType);
        Assert.True(ApprovedSalesInvoiceComparison.Matches(invoice, snapshot));
        Assert.Equal(before, row.SnapshotJson);
        typeof(SalesInvoiceDetails).GetProperty(nameof(SalesInvoiceDetails.Data))!.SetValue(invoice.Details, invoice.ToDraft().Details with { FactorBankAccounts = [new("factor/123")] });
        Assert.False(ApprovedSalesInvoiceComparison.Matches(invoice, snapshot));
    }

    [Fact]
    public void IssuanceValidationAlsoChecksPersistedBankAccountData()
    {
        var invoice = Create();
        typeof(SalesInvoiceDetails).GetProperty(nameof(SalesInvoiceDetails.Data))!.SetValue(invoice.Details, invoice.ToDraft().Details with
        { FactorBankAccounts = [new("1234567890", "invalid", BankOwnAccountType: (BankOwnAccountType)0)] });
        var validation = new SalesInvoiceIssuanceValidator(new()).Validate(invoice);
        Assert.False(validation.IsReadyForIssuance);
        Assert.Contains(validation.Errors, x => x.Path == "details.factorBankAccounts[0].swift" && x.Code == "BankAccountSwiftInvalid");
        Assert.Contains(validation.Errors, x => x.Path == "details.factorBankAccounts[0].bankOwnAccountType" && x.Code == "BankOwnAccountTypeInvalid");
    }

    [Theory]
    [InlineData("{")][InlineData("null")][InlineData("[]")]
    public void MalformedJsonProducesControlledError(string json)
    {
        var row = Approve(Create()); typeof(SalesInvoiceApproval).GetProperty(nameof(SalesInvoiceApproval.SnapshotJson))!.SetValue(row, json);
        Assert.Equal("SnapshotInvalid", Assert.Throws<SalesInvoiceApprovalSnapshotException>(() => Reader.Read(row)).Code);
    }

    [Theory]
    [InlineData("buyer")][InlineData("quantity")][InlineData("amounts")][InlineData("duplicate")]
    public void RequiredDataAndArithmeticAreStillValidated(string change)
    {
        var row = Approve(Create()); var json = JsonNode.Parse(row.SnapshotJson)!;
        if (change == "buyer") json["Input"]!.AsObject().Remove("Buyer");
        if (change == "quantity") json["Input"]!["Lines"]![0]!["Quantity"] = null;
        if (change == "amounts") json["Calculations"]!["Totals"]!["Vat"] = "999.99";
        var text = json.ToJsonString();
        if (change == "duplicate") text = text.Replace("\"SourceRevision\":1", "\"SourceRevision\":1,\"SourceRevision\":1", StringComparison.Ordinal);
        typeof(SalesInvoiceApproval).GetProperty(nameof(SalesInvoiceApproval.SnapshotJson))!.SetValue(row, text);
        Assert.Throws<SalesInvoiceApprovalSnapshotException>(() => Reader.Read(row));
    }

    [Theory]
    [InlineData("tenant")][InlineData("invoice")][InlineData("approval")][InlineData("revision")][InlineData("actor")][InlineData("time")]
    public void SnapshotIsBoundToItsExactApprovalAndTenant(string change)
    {
        var row = Approve(Create());
        var name = change switch { "tenant" => "TenantId", "invoice" => "SalesInvoiceId", "approval" => "Id", "revision" => "SourceRevision", "actor" => "ApprovedBy", _ => "ApprovedAt" };
        object value = change switch { "revision" => row.SourceRevision + 1, "actor" => "other", "time" => Now.AddMinutes(1), _ => Guid.NewGuid() };
        typeof(SalesInvoiceApproval).GetProperty(name)!.SetValue(row, value);
        Assert.Equal("SnapshotIdentityMismatch", Assert.Throws<SalesInvoiceApprovalSnapshotException>(() => Reader.Read(row)).Code);
    }

    [Fact]
    public void SnapshotInputContractsCoverEveryCurrentDomainInputProperty()
    {
        static void Covers(Type domain, Type snapshot) => Assert.All(domain.GetProperties().Where(x => x.Name != "IsEmpty"), x => Assert.NotNull(snapshot.GetProperty(x.Name)));
        Covers(typeof(SalesInvoiceDraft), typeof(ApprovedSalesInvoiceDraft)); Covers(typeof(SalesInvoiceLineInput), typeof(ApprovedSalesInvoiceLine));
        Covers(typeof(SalesInvoicePartyData), typeof(ApprovedInvoiceParty)); Covers(typeof(QualificationDecisionData), typeof(ApprovedQualificationDecision));
        Covers(typeof(InvoiceQualificationsData), typeof(ApprovedQualifications)); Covers(typeof(SalesInvoiceFa3Data), typeof(ApprovedFa3Data));
        Covers(typeof(InvoiceTaxTreatmentData), typeof(ApprovedTaxTreatment)); Covers(typeof(InvoiceExemptionBasisData), typeof(ApprovedExemptionBasis));
        Covers(typeof(InvoiceParticipantData), typeof(ApprovedParticipant)); Covers(typeof(ExchangeRateData), typeof(ApprovedExchangeRate));
    }
}
