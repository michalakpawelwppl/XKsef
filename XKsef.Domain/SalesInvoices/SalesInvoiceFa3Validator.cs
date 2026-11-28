using System.Text.RegularExpressions;

namespace xKsef.Domain.SalesInvoices;

public static class SalesInvoiceFa3Validator
{
    public static IEnumerable<(string Path, QualificationDecisionData Decision)> Decisions(SalesInvoiceDraft draft)
    {
        foreach (var item in draft.Fa3.Qualifications.Decisions()) yield return ($"fa3.qualifications.{item.Name}", item.Decision);
        if (draft.Seller is { } seller) yield return ("seller.plPrefix", seller.PlPrefix);
        if (draft.Buyer is { } buyer) yield return ("buyer.plPrefix", buyer.PlPrefix);
        foreach (var line in draft.Lines) yield return ($"lines[{line.Id}].annex15", line.Annex15);
        foreach (var participant in draft.Participants) yield return ($"participants[{participant.Id}].party.plPrefix", participant.Party.PlPrefix);
        if (draft.Fa3.AuthorizedParty is { } authorized) yield return ("fa3.authorizedParty.party.plPrefix", authorized.Party.PlPrefix);
    }

    public static void Validate(SalesInvoiceDraft draft, List<InvoiceValidationIssue> issues)
    {
        void Check(bool valid, string path, string message) { if (!valid) issues.Add(new(path, "InvalidFa3Data", message)); }
        void Text(string? value, int max, string path) => Check(value is null || value.Length <= max, path, $"Maksymalna długość: {max}.");
        void Date(DateOnly? value, string path) => Check(value is null || value >= new DateOnly(2006, 1, 1) && value <= new DateOnly(2050, 1, 1), path, "Data poza zakresem FA(3).");
        Check(draft.ModelVersion is 1 or 2, "modelVersion", "Nieobsługiwana wersja modelu.");
        Text(draft.IssuePlace, 256, "issuePlace");
        Check(draft.SaleDateMode is null || Enum.IsDefined(draft.SaleDateMode.Value), "saleDateMode", "Nieprawidłowy tryb daty sprzedaży.");
        if (draft.SalePeriod is { } period)
        {
            Check(period.Version == 1, "salePeriod", "Nieobsługiwana wersja okresu sprzedaży.");
            Date(period.From, "salePeriod.from"); Date(period.To, "salePeriod.to");
            Check(period.From is null || period.To is null || period.From <= period.To, "salePeriod", "Początek okresu nie może być późniejszy od końca.");
        }
        Check(draft.Fa3 is { Version: 1, WarehouseDocumentNumbers: not null, Qualifications: not null }, "fa3", "Nieobsługiwany lub niekompletny format sekcji FA(3).");
        Check(draft.Participants is not null && draft.Participants.Count <= 100, "participants", "Maksymalnie 100 uczestników.");
        if (draft.Fa3 is null || draft.Fa3.Qualifications is null || draft.Fa3.WarehouseDocumentNumbers is null || draft.Participants is null) return;
        Check(draft.Fa3.WarehouseDocumentNumbers.Count <= 1000, "fa3.warehouseDocumentNumbers", "Maksymalnie 1000 dokumentów WZ.");
        foreach (var number in draft.Fa3.WarehouseDocumentNumbers) Check(!string.IsNullOrWhiteSpace(number) && number.Length <= 256, "fa3.warehouseDocumentNumbers", "Numer WZ musi zawierać 1–256 znaków.");

        void Party(SalesInvoicePartyData? party, string path, bool internalIdAllowed)
        {
            if (party is null) { Check(false, path, "Dane uczestnika nie mogą być null."); return; }
            Text(party.BuyerLinkKey, 32, path + ".buyerLinkKey"); Text(party.CustomerNumber, 256, path + ".customerNumber");
            Text(party.Name, 512, path + ".name"); Text(party.Identifier, 50, path + ".identifier"); Text(party.Eori, 256, path + ".eori");
            Check(Enum.IsDefined(party.IdentifierKind) && (internalIdAllowed || party.IdentifierKind != TaxIdentifierKind.InternalId), path + ".identifierKind", "Nieprawidłowa gałąź identyfikacji.");
            Check(party.SellerStatus is null || Enum.IsDefined(party.SellerStatus.Value), path + ".sellerStatus", "Nieprawidłowy status podatnika.");
            Check(Enum.IsDefined(party.TaxpayerStatus), path + ".taxpayerStatus", "Nieprawidłowa kwalifikacja podatnika.");
            Check(party.Contacts is not null && party.Contacts.Count <= 3 && party.Contacts.All(x => x is not null), path + ".contacts", "Maksymalnie 3 niepuste kontakty.");
            foreach (var contact in party.Contacts ?? []) if (contact is not null) { Text(contact.Email, 255, path + ".contacts.email"); Text(contact.Phone, 16, path + ".contacts.phone"); }
            foreach (var address in new[] { party.Address, party.CorrespondenceAddress }) if (address is not null)
            {
                Check(address.CountryCode is not null && Regex.IsMatch(address.CountryCode, "^[A-Z]{2}$"), path + ".address.countryCode", "Nieprawidłowy kod kraju.");
                Text(address.Line1, 512, path + ".address.line1"); Text(address.Line2, 512, path + ".address.line2"); Text(address.Gln, 13, path + ".address.gln");
            }
        }
        if (draft.Seller is not null) Party(draft.Seller, "seller", false);
        if (draft.Buyer is not null) Party(draft.Buyer, "buyer", false);
        var ids = new HashSet<Guid>();
        foreach (var participant in draft.Participants)
        {
            if (participant is null) { Check(false, "participants", "Uczestnik nie może być null."); continue; }
            var path = $"participants[{participant.Id}]";
            Check(participant.Id != Guid.Empty && ids.Add(participant.Id), path, "Identyfikator uczestnika musi być niepusty i unikalny.");
            Check(participant.Role is null || Enum.IsDefined(participant.Role.Value), path + ".role", "Nieprawidłowa rola.");
            Text(participant.OtherRoleDescription, 256, path + ".otherRoleDescription");
            Check(participant.SharePercent is null || participant.SharePercent >= 0 && participant.SharePercent <= 100 && Calculation.FinancialMath.HasScale(participant.SharePercent.Value, 6), path + ".sharePercent", "Udział wymaga 0–100%, do 6 miejsc.");
            Party(participant.Party, path + ".party", true);
        }
        if (draft.Fa3.AuthorizedParty is { } authorized)
        {
            Check(authorized.Role is null || Enum.IsDefined(authorized.Role.Value), "fa3.authorizedParty.role", "Nieprawidłowa rola podmiotu upoważnionego.");
            Party(authorized.Party, "fa3.authorizedParty.party", false);
        }
        foreach (var line in draft.Lines ?? []) if (line is not null)
        {
            var path = $"lines[{line.Id}]";
            Date(line.SaleDate, path + ".saleDate"); Text(line.Pkob, 50, path + ".pkob");
            Check(line.Procedure is null || Enum.IsDefined(line.Procedure.Value), path + ".procedure", "Nieprawidłowa procedura.");
            Check(line.ExciseAmount is null || line.ExciseAmount >= 0 && line.ExciseAmount <= Calculation.FinancialMath.MaxAmount && Calculation.FinancialMath.HasScale(line.ExciseAmount.Value, 2), path + ".exciseAmount", "Nieprawidłowa kwota akcyzy.");
            if (line.TaxTreatment is { } tax)
            {
                Check(tax.Version == 1 && (tax.Kind is null || Enum.IsDefined(tax.Kind.Value)), path + ".taxTreatment", "Nieprawidłowy format kwalifikacji podatkowej.");
                Check(tax.DomesticRateCode is null || new[] { "23", "22", "8", "7", "5", "4", "3" }.Contains(tax.DomesticRateCode), path + ".taxTreatment.domesticRateCode", "Nieprawidłowa stawka krajowa.");
                Check(tax.ForeignVatRate is null || tax.ForeignVatRate >= 0 && tax.ForeignVatRate <= 100 && Calculation.FinancialMath.HasScale(tax.ForeignVatRate.Value, 6), path + ".taxTreatment.foreignVatRate", "Nieprawidłowa stawka zagraniczna.");
                Check(tax.TaxCountryCode is null || Regex.IsMatch(tax.TaxCountryCode, "^[A-Z]{2}$"), path + ".taxTreatment.taxCountryCode", "Nieprawidłowy kraj opodatkowania.");
            }
            if (line.ExemptionBasis is { } basis)
            {
                Check(basis.Version == 1 && (basis.Kind is null || Enum.IsDefined(basis.Kind.Value)), path + ".exemptionBasis", "Nieprawidłowy format podstawy zwolnienia.");
                Text(basis.Text, 256, path + ".exemptionBasis.text");
            }
        }
        // Do not enumerate paths through invalid nested structures.
        if (issues.Count != 0) return;
        foreach (var (path, decision) in Decisions(draft))
        {
            Check(decision is not null, path, "Kwalifikacja nie może być null.");
            if (decision is null) continue;
            Check(Enum.IsDefined(decision.Value) && Enum.IsDefined(decision.Source) && (decision.SuggestedValue is null || Enum.IsDefined(decision.SuggestedValue.Value)), path, "Nieprawidłowa kwalifikacja.");
            Check(decision.Source != QualificationSource.UserConfirmed || decision.Value != QualificationValue.Unknown, path, "Nie można potwierdzić wartości Nieustalone.");
            Text(decision.Reason, 1000, path + ".reason");
        }
    }
}
