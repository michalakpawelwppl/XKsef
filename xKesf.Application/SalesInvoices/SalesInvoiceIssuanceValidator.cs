using System.Xml;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Application.SalesInvoices;

public sealed record SalesInvoiceIssuanceValidationResult(IReadOnlyList<InvoiceValidationIssue> Errors, IReadOnlyList<InvoiceValidationIssue> Warnings, InvoiceCalculationResult? Calculations = null)
{
    public bool IsReadyForIssuance => Errors.Count == 0;
}

/// <summary>Read-only validation of the data represented by the basic VAT invoice model, before numbering and XML generation.</summary>
public sealed class SalesInvoiceIssuanceValidator(SalesInvoiceCalculator calculator)
{
    // Closed vocabulary from docs/analysis/fa3-sources/KodyKrajow_v10-0E.xsd, not a live lookup.
    private static readonly HashSet<string> Fa3CountryCodes = "AF AX AL DZ AD AO AI AQ AG AN SA AR AM AW AU AT AZ BS BH BD BB BE BZ BJ BM BT BY BO BQ BA BW BR BN IO BG BF BI XC CL CN HR CW CY TD ME DK DM DO DJ EG EC ER EE ET FK FJ PH FI FR TF GA GM GH GI GR GD GL GE GU GG GY GF GP GT GN GQ GW HT ES HN HK IN ID IQ IR IE IS IL JM JP YE JE JO KY KH CM CA QA KZ KE KG KI CO KM CG CD KP XK CR CU KW LA LS LB LR LY LI LT LV LU MK MG YT MO MW MV MY ML MT MP MA MQ MR MU MX XL FM UM MD MC MN MS MZ MM NA NR NP NL DE NE NG NI NU NF NO NC NZ PS OM PK PW PA PG PY PE PN PF PL GS PT PR CF CZ KR ZA RE RU RO RW EH BL KN LC MF VC SV WS AS SM SN RS SC SL SG SK SI SO LK PM US SZ SD SS SR SJ SH SY CH SE TJ TH TW TZ TG TK TO TT TN TR TM TV UG UA UY UZ VU WF VA HU VE GB VN IT TL CI BV CX IM SX CK VI VG HM CC MH FO SB ST TC ZM CV ZW AE XI".Split(' ').ToHashSet(StringComparer.Ordinal);

    public SalesInvoiceIssuanceValidationResult Validate(SalesInvoice invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        var errors = new List<InvoiceValidationIssue>();
        var warnings = new List<InvoiceValidationIssue>();
        void Error(string path, string code, string message) => errors.Add(new(path, code, message));
        void Required(string? value, string path) { if (string.IsNullOrWhiteSpace(value)) Error(path, "Required", "Uzupełnij wymagane pole."); }
        void XmlText(string? value, string path)
        {
            if (value is null) return;
            try { XmlConvert.VerifyXmlChars(value); }
            catch (XmlException) { Error(path, "InvalidXmlCharacters", "Pole zawiera znaki niedozwolone w XML."); }
        }
        var draft = invoice.ToDraft();
        InvoiceSettlementCalculator.ValidateForIssuance(draft.Details.Settlement, invoice.GrossAmount, invoice.SettlementAmounts, errors);

        // Reuse draft validation, including VAT vocabulary, scales, ranges and calculation rules.
        InvoiceCalculationResult? expected = null;
        try { expected = calculator.Calculate(draft); }
        catch (SalesInvoiceValidationException exception) { errors.AddRange(exception.Issues.Select(x => x.Path.EndsWith(".taxCode", StringComparison.Ordinal) ? x with { Code = "InvalidTaxCode" } : x)); }
        errors.AddRange(SalesInvoiceCalculator.GetMissingData(draft));
        if (!invoice.CalculationComplete || invoice.NetAmount is null || invoice.VatAmount is null || invoice.GrossAmount is null || invoice.VatAmountPln is null)
            Error("totals", "CalculationIncomplete", "Brak kompletnych obliczeń netto, VAT, brutto lub VAT w PLN.");

        void Party(SalesInvoicePartyData? party, string path, bool seller)
        {
            if (party is null) { Error(path, "PartyRequired", seller ? "Uzupełnij sprzedawcę." : "Uzupełnij nabywcę."); return; }
            Required(party.Name, path + ".name");
            XmlText(party.Name, path + ".name"); XmlText(party.Identifier, path + ".identifier"); XmlText(party.Eori, path + ".eori");
            for (var i = 0; i < party.Contacts.Count; i++)
            {
                XmlText(party.Contacts[i].Email, $"{path}.contacts[{i}].email"); XmlText(party.Contacts[i].Phone, $"{path}.contacts[{i}].phone");
            }
            if (seller && party.IdentifierKind != TaxIdentifierKind.Nip) Error(path + ".identifierKind", "SellerNipRequired", "Sprzedawca musi posiadać polski NIP.");
            if (party.IdentifierKind != TaxIdentifierKind.None) Required(party.Identifier, path + ".identifier");
            try { SalesInvoiceService.ValidateIdentifier(party, path); }
            catch (SalesInvoiceValidationException exception) { errors.AddRange(exception.Issues.Select(x => x with { Code = "InvalidTaxIdentifier" })); }
            if (party.IdentifierCountryCode is { } country && party.IdentifierKind == TaxIdentifierKind.Other && !Fa3CountryCodes.Contains(country))
                Error(path + ".identifierCountryCode", "InvalidCountryCode", "Kod kraju identyfikatora nie należy do słownika FA(3).");
            void Address(InvoiceAddress? address, string addressPath, bool required)
            {
                if (address is null) { if (required) Error(addressPath, "AddressRequired", "Uzupełnij adres sprzedawcy."); return; }
                Required(address.Line1, addressPath + ".line1");
                XmlText(address.Line1, addressPath + ".line1"); XmlText(address.Line2, addressPath + ".line2"); XmlText(address.Gln, addressPath + ".gln");
                if (!Fa3CountryCodes.Contains(address.CountryCode ?? "")) Error(addressPath + ".countryCode", "InvalidCountryCode", "Kod kraju adresu nie należy do słownika FA(3).");
            }
            Address(party.Address, path + ".address", seller);
            Address(party.CorrespondenceAddress, path + ".correspondenceAddress", false);
            if (!seller && party.Address is null) warnings.Add(new(path + ".address", "BuyerAddressMissing", "Brak adresu nabywcy. FA(3) dopuszcza pominięcie adresu; sprawdź wymagania dla danej transakcji."));
        }
        Party(draft.Seller, "seller", true);
        Party(draft.Buyer, "buyer", false);
        // New approvals always use the complete v2 contract, including explicit qualifications.
        {
            XmlText(draft.IssuePlace, "issuePlace");
            for (var i = 0; i < draft.Fa3.WarehouseDocumentNumbers.Count; i++) XmlText(draft.Fa3.WarehouseDocumentNumbers[i], $"fa3.warehouseDocumentNumbers[{i}]");
            if (draft.SaleDateMode is null or SaleDateMode.Unknown) Error("saleDateMode", "SaleDateModeUnresolved", "Ustal sposób określenia daty sprzedaży.");
            if (draft.SaleDateMode == SaleDateMode.CommonDate && draft.SaleDate is null) Error("saleDate", "SaleDateRequired", "Podaj wspólną datę sprzedaży.");
            if (draft.SaleDateMode == SaleDateMode.Period && (draft.SalePeriod?.From is null || draft.SalePeriod.To is null)) Error("salePeriod", "SalePeriodIncomplete", "Podaj obie daty okresu sprzedaży.");
            if (draft.SaleDateMode != SaleDateMode.Period && draft.SalePeriod is not null) Error("salePeriod", "SaleDateModeConflict", "Okres sprzedaży nie odpowiada wybranemu trybowi dat.");
            if (draft.SaleDateMode is SaleDateMode.Period or SaleDateMode.PerLine or SaleDateMode.NotDetermined && draft.SaleDate is not null) Error("saleDate", "SaleDateModeConflict", "Wspólna data nie odpowiada wybranemu trybowi dat.");
            if (draft.SaleDateMode == SaleDateMode.SameAsIssueDate && draft.SaleDate is not null && draft.SaleDate != draft.PlannedIssueDate) Error("saleDate", "SaleDateModeConflict", "Data sprzedaży powinna być zgodna z datą wystawienia.");
            foreach (var (path, decision) in SalesInvoiceFa3Validator.Decisions(draft))
            {
                if (path.Contains("fa3.qualifications", StringComparison.Ordinal) && !path.EndsWith("Article42Paragraph5Applies", StringComparison.Ordinal))
                {
                    if (decision.Value == QualificationValue.Unknown) Error(path, "QualificationUnresolved", "Kwalifikacja pozostaje nieustalona.");
                    else if (decision.Source is not (QualificationSource.UserConfirmed or QualificationSource.DeterministicRule)) Error(path, "QualificationNotConfirmed", "Potwierdź kwalifikację.");
                }
                else if (decision.Value != QualificationValue.Unknown && decision.Source is not (QualificationSource.UserConfirmed or QualificationSource.DeterministicRule))
                    Error(path, "QualificationNotConfirmed", "Potwierdź zastosowaną kwalifikację.");
            }
            foreach (var participant in draft.Participants)
            {
                var path = $"participants[{participant.Id}]";
                Party(participant.Party, path + ".party", false);
                if (participant.Role is null) Error(path + ".role", "ParticipantRoleRequired", "Wybierz rolę uczestnika.");
                if (participant.Role == InvoiceParticipantRole.Other) Required(participant.OtherRoleDescription, path + ".otherRoleDescription");
                XmlText(participant.OtherRoleDescription, path + ".otherRoleDescription");
            }
            if (draft.Participants.Where(x => x.Role == InvoiceParticipantRole.AdditionalBuyer).Sum(x => x.SharePercent ?? 0) > 100) Error("participants", "ParticipantSharesInvalid", "Suma udziałów dodatkowych nabywców przekracza 100%.");
            if (draft.Fa3.AuthorizedParty is { } authorized)
            {
                Party(authorized.Party, "fa3.authorizedParty.party", true);
                if (authorized.Role is null) Error("fa3.authorizedParty.role", "AuthorizedPartyRoleRequired", "Wybierz rolę podmiotu upoważnionego.");
            }
            if (draft.Fa3.Qualifications.SelfBilling.Value == QualificationValue.Yes)
                Error("fa3.qualifications.SelfBilling", "SelfBillingNotSupported", "Samofakturowanie wymaga niewdrożonej kontroli porozumienia i uprawnień wystawcy.");
            if (draft.Fa3.Qualifications.HasNewMeansOfTransport.Value == QualificationValue.Yes)
                Error("fa3.qualifications.HasNewMeansOfTransport", "NewTransportDataNotSupported", "Model nie przechowuje jeszcze wymaganych danych nowego środka transportu.");
            var exempt = draft.Lines.Any(x => x.TaxCode == "zw");
            if (draft.Fa3.Qualifications.HasExemptSales.Value != QualificationValue.Unknown && (draft.Fa3.Qualifications.HasExemptSales.Value == QualificationValue.Yes) != exempt)
                Error("fa3.qualifications.HasExemptSales", "QualificationConflict", "Kwalifikacja sprzedaży zwolnionej jest sprzeczna z pozycjami.");
            var reverse = draft.Lines.Any(x => x.TaxCode == "oo");
            if (draft.Fa3.Qualifications.ReverseCharge.Value != QualificationValue.Unknown && (draft.Fa3.Qualifications.ReverseCharge.Value == QualificationValue.Yes) != reverse)
                Error("fa3.qualifications.ReverseCharge", "QualificationConflict", "Kwalifikacja odwrotnego obciążenia jest sprzeczna z pozycjami.");
        }
        if (draft.PlannedIssueDate is null) Error("plannedIssueDate", "IssueDateRequired", "Podaj planowaną datę wystawienia faktury.");
        // FA(3) P_6 is optional: the model has one common sale date, no periods or per-line dates.
        if (draft.SaleDate is null) warnings.Add(new("saleDate", "SaleDateUnspecified", "Nie podano odrębnej daty sprzedaży. Pole P_6 jest opcjonalne; model nie rozróżnia daty nieustalonej i zgodnej z datą wystawienia."));
        if (draft.Lines.Count == 0) Error("lines", "LinesRequired", "Dodaj co najmniej jedną pozycję faktury.");
        foreach (var line in draft.Lines)
        {
            var path = $"lines[{line.Id}]";
            Required(line.Description, path + ".description");
            Required(line.Unit, path + ".unit");
            XmlText(line.Description, path + ".description"); XmlText(line.Unit, path + ".unit");
            XmlText(line.ProductCode, path + ".productCode"); XmlText(line.Gtin, path + ".gtin"); XmlText(line.Pkwiu, path + ".pkwiu"); XmlText(line.Cn, path + ".cn");
            if (line.TaxCode == "zw" && line.ExemptionBasis is null) Error(path + ".taxCode", "ExemptionBasisNotRepresented", "Kwalifikacja zw wymaga podstawy zwolnienia (P_19A, P_19B lub P_19C).");
            if (line.ExemptionBasis is { } basis)
            {
                if (basis.Kind is null) Error(path + ".exemptionBasis.kind", "ExemptionKindRequired", "Wybierz rodzaj podstawy zwolnienia.");
                Required(basis.Text, path + ".exemptionBasis.text"); XmlText(basis.Text, path + ".exemptionBasis.text");
            }
            XmlText(line.Pkob, path + ".pkob");
            if (draft.SaleDateMode == SaleDateMode.PerLine && line.SaleDate is null) Error(path + ".saleDate", "LineSaleDateRequired", "Podaj datę sprzedaży pozycji.");
            if (draft.SaleDateMode != SaleDateMode.PerLine && line.SaleDate is not null) Error(path + ".saleDate", "SaleDateModeConflict", "Data pozycji wymaga trybu dat sprzedaży poszczególnych pozycji.");
        }
        if (expected is { IsComplete: true }) ValidateAmounts(invoice, expected, errors);
        if (invoice.CalculationVersion != InvoiceCalculationResult.AlgorithmVersion) Error("calculationVersion", "CalculationVersionOutdated", "Przelicz projekt aktualną wersją algorytmu.");
        for (var i = 0; i < draft.Details.PaymentTerms.Count; i++)
        {
            var term = draft.Details.PaymentTerms[i];
            var path = $"details.paymentTerms[{i}]";
            XmlText(term.Unit, path + ".unit"); XmlText(term.StartingEvent, path + ".startingEvent");
            if (term.DueDate is { } due && (due < new DateOnly(2016, 7, 1) || due > new DateOnly(2050, 1, 1))) Error(path + ".dueDate", "InvalidPaymentDate", "Termin płatności jest poza zakresem FA(3).");
            if ((term.Quantity is not null || term.Unit is not null || term.StartingEvent is not null) && (term.Quantity is null || string.IsNullOrWhiteSpace(term.Unit) || string.IsNullOrWhiteSpace(term.StartingEvent)))
                Error(path, "PaymentTermDescriptionIncomplete", "Opis terminu wymaga ilości, jednostki i zdarzenia początkowego.");
        }
        for (var i = 0; i < draft.Details.AdditionalDescriptions.Count; i++)
        {
            Required(draft.Details.AdditionalDescriptions[i].Key, $"details.additionalDescriptions[{i}].key");
            Required(draft.Details.AdditionalDescriptions[i].Value, $"details.additionalDescriptions[{i}].value");
            XmlText(draft.Details.AdditionalDescriptions[i].Key, $"details.additionalDescriptions[{i}].key");
            XmlText(draft.Details.AdditionalDescriptions[i].Value, $"details.additionalDescriptions[{i}].value");
        }
        XmlText(draft.Notes, "notes"); XmlText(draft.Details.OtherPaymentDescription, "details.otherPaymentDescription");
        // Both account collections (including XML characters) are checked by the shared domain validator above.
        return new(errors.Distinct().ToArray(), warnings.ToArray(), expected);
    }

    private static void ValidateAmounts(SalesInvoice invoice, InvoiceCalculationResult expected, List<InvoiceValidationIssue> errors)
    {
        if (invoice.NetAmount != expected.Net || invoice.VatAmount != expected.Vat || invoice.GrossAmount != expected.Gross || invoice.VatAmountPln != expected.VatPln || invoice.GlobalDiscountAmount != expected.GlobalDiscountAmount)
            errors.Add(new("totals", "TotalsMismatch", "Zapisane sumy nie są zgodne z obliczeniami pozycji faktury."));
        var calculated = expected.Lines.ToDictionary(x => x.Id);
        foreach (var line in invoice.Lines)
        {
            var value = calculated[line.Id];
            if (line.NetAmount != value.Net || line.VatAmount != value.Vat || line.GrossAmount != value.Gross || line.VatAmountPln != value.VatPln || line.GlobalDiscountAllocation != value.GlobalDiscountAllocation || line.VatRoundingAdjustment != value.VatRoundingAdjustment || line.VatAmountOrigin != value.VatAmountOrigin || line.VatBaseRoundingAdjustment != value.VatBaseRoundingAdjustment)
                errors.Add(new($"lines[{line.Id}]", "LineAmountsMismatch", "Zapisane kwoty pozycji nie są zgodne z jej danymi i metodą obliczeń."));
        }
        var summaries = invoice.TaxSummaries.Select(x => new CalculatedTaxSummary(x.TaxCode, x.NetAmount, x.VatAmount, x.GrossAmount, x.VatAmountPln, x.CalculationDetails)).OrderBy(x => x.TaxCode, StringComparer.Ordinal);
        if (!summaries.SequenceEqual(expected.TaxSummaries.OrderBy(x => x.TaxCode, StringComparer.Ordinal)))
            errors.Add(new("taxSummaries", "TaxSummariesMismatch", "Podsumowania VAT nie są zgodne z pozycjami faktury."));
    }
}
