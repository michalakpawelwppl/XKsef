using System.Text.RegularExpressions;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Domain.SalesInvoices;

public static class SalesInvoiceValidator
{
    // Closed currency vocabulary of FA(3), pattern 13775 / 1-0E. Not a live provider lookup.
    private static readonly HashSet<string> CurrencyCodes = "AED AFN ALL AMD ANG AOA ARS AUD AWG AZN BAM BBD BDT BGN BHD BIF BMD BND BOB BOV BRL BSD BTN BWP BYN BZD CAD CDF CHE CHF CHW CLF CLP CNY COP COU CRC CUC CUP CVE CZK DJF DKK DOP DZD EGP ERN ETB EUR FJD FKP GBP GEL GGP GHS GIP GMD GNF GTQ GYD HKD HNL HRK HTG HUF IDR ILS IMP INR IQD IRR ISK JEP JMD JOD JPY KES KGS KHR KMF KPW KRW KWD KYD KZT LAK LBP LKR LRD LSL LYD MAD MDL MGA MKD MMK MNT MOP MRU MUR MVR MWK MXN MXV MYR MZN NAD NGN NIO NOK NPR NZD OMR PAB PEN PGK PHP PKR PLN PYG QAR RON RSD RUB RWF SAR SBD SCR SDG SEK SGD SHP SLL SOS SRD SSP STN SVC SYP SZL THB TJS TMT TND TOP TRY TTD TWD TZS UAH UGX USD USN UYI UYU UYW UZS VES VND VUV WST XAF XAG XAU XBA XBB XBC XBD XCD XCG XDR XOF XPD XPF XPT XSU XUA XXX YER ZAR ZMW ZWL".Split(' ').ToHashSet(StringComparer.Ordinal);
    public static void Validate(SalesInvoiceDraft draft)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var issues = new List<InvoiceValidationIssue>();
        void Check(bool condition, string path, string message) { if (!condition) issues.Add(new(path, "Invalid", message)); }
        void Text(string? value, int length, string path) => Check(value?.Length <= length || value is null, path, $"Maksymalna długość: {length}.");
        void Number(decimal? value, int scale, decimal max, string path) => Check(value is null || value >= 0 && value <= max && FinancialMath.HasScale(value.Value, scale), path, $"Wymagana nieujemna liczba w zakresie do {max}, skala do {scale}.");
        void Discount(InvoiceDiscount? value, string path)
        {
            if (value is null) return;
            Check(Enum.IsDefined(value.Kind), path, "Nieprawidłowy rodzaj rabatu.");
            Number(value.Value, value.Kind == DiscountKind.Percent ? 6 : 8, value.Kind == DiscountKind.Percent ? 100m : 99999999999999.99999999m, path);
        }
        Check(draft.Kind == SalesInvoiceKind.Vat, "kind", "Etap 1 obsługuje wyłącznie zwykłe faktury VAT.");
        Check(Enum.IsDefined(draft.VatMethod), "vatMethod", "Nieprawidłowa metoda VAT.");
        Check(Enum.IsDefined(draft.GlobalDiscountBasis), "globalDiscountBasis", "Nieprawidłowa podstawa rabatu.");
        Check(Enum.IsDefined(draft.PdfLanguage), "pdfLanguage", "Nieprawidłowy język PDF.");
        Check(draft.Currency is not null && CurrencyCodes.Contains(draft.Currency.Trim().ToUpperInvariant()), "currency", "Nieobsługiwany kod waluty FA(3).");
        Text(draft.Notes, 4000, "notes");
        Discount(draft.GlobalDiscount, "globalDiscount");
        if (draft.GlobalDiscount?.Kind == DiscountKind.Amount) Number(draft.GlobalDiscount.Value, 2, FinancialMath.MaxAmount, "globalDiscount");
        foreach (var date in new[] { draft.PlannedIssueDate, draft.SaleDate }) Check(date is null || date >= new DateOnly(2016, 7, 1) && date <= new DateOnly(2050, 1, 1), "date", "Data jest poza zakresem FA(3).");
        Check(draft.Lines is not null && draft.Lines.Count <= 10000, "lines", "Nieprawidłowa kolekcja pozycji (maks. 10000).");
        var ids = new HashSet<Guid>();
        foreach (var line in draft.Lines ?? [])
        {
            if (line is null) { Check(false, "lines", "Pozycja nie może być null."); continue; }
            var path = $"lines[{line.Id}]";
            Check(line.Id != Guid.Empty && ids.Add(line.Id), path, "Identyfikatory pozycji muszą być niepuste i unikalne.");
            Check(Enum.IsDefined(line.PriceBasis), path + ".priceBasis", "Nieprawidłowa podstawa ceny.");
            Check(line.TaxCode is not null && SalesInvoiceCalculator.TaxRates.ContainsKey(line.TaxCode), path + ".taxCode", "Nieobsługiwana kwalifikacja VAT.");
            Number(line.Quantity, 6, 9999999999999999.999999m, path + ".quantity");
            Number(line.UnitPrice, 8, 99999999999999.99999999m, path + ".unitPrice");
            Discount(line.Discount, path + ".discount");
            Text(line.Description, 512, path + ".description"); Text(line.Unit, 256, path + ".unit");
            Text(line.ProductCode, 50, path + ".productCode"); Text(line.Gtin, 20, path + ".gtin"); Text(line.Pkwiu, 50, path + ".pkwiu"); Text(line.Cn, 50, path + ".cn");
            Check(line.Gtu is null || Regex.IsMatch(line.Gtu, "^GTU_(0[1-9]|1[0-3])$"), path + ".gtu", "Nieprawidłowy kod GTU.");
        }
        foreach (var (party, path) in new[] { (draft.Seller, "seller"), (draft.Buyer, "buyer") })
        {
            if (party is null) continue;
            Text(party.Name, 512, path + ".name"); Text(party.Identifier, 50, path + ".identifier"); Text(party.Eori, 256, path + ".eori");
            Check(Enum.IsDefined(party.IdentifierKind), path + ".identifierKind", "Nieprawidłowy rodzaj identyfikatora.");
            Check(party.Contacts is not null && party.Contacts.Count <= 3, path + ".contacts", "Maksymalnie 3 kontakty.");
            foreach (var contact in party.Contacts ?? [])
            {
                if (contact is null) { Check(false, path + ".contacts", "Kontakt nie może być null."); continue; }
                Text(contact.Email, 255, path + ".email"); Text(contact.Phone, 16, path + ".phone");
            }
            foreach (var address in new[] { party.Address, party.CorrespondenceAddress })
            {
                if (address is null) continue;
                Check(address.CountryCode is not null && Regex.IsMatch(address.CountryCode, "^[A-Z]{2}$"), path + ".address.countryCode", "Nieprawidłowy kraj.");
                Text(address.Line1, 512, path + ".address.line1"); Text(address.Line2, 512, path + ".address.line2"); Text(address.Gln, 13, path + ".address.gln");
            }
        }
        Check(draft.ExchangeRates is not null && draft.ExchangeRates.Count <= 1, "exchangeRates", "Etap 1 dopuszcza jeden ręczny kurs dokumentu.");
        foreach (var rate in draft.ExchangeRates ?? [])
        {
            if (rate is null) { Check(false, "exchangeRates", "Kurs nie może być null."); continue; }
            Number(rate.Rate, 12, 9999999999999999.999999999999m, "exchangeRates.rate");
            Number(rate.OriginalRate, 12, 9999999999999999.999999999999m, "exchangeRates.originalRate");
            Number(rate.QuotedUnits, 6, 9999999999999999.999999m, "exchangeRates.quotedUnits");
            Check(rate.Id != Guid.Empty && rate.Rate > 0 && rate.QuotedUnits > 0, "exchangeRates", "Kurs, jednostka i identyfikator muszą być dodatnie/niepuste.");
            Check(rate.FromCurrency == draft.Currency?.Trim().ToUpperInvariant() && rate.ToCurrency == "PLN", "exchangeRates", "Kurs musi przeliczać walutę dokumentu do PLN.");
            Check(Enum.IsDefined(rate.Source), "exchangeRates.source", "Nieprawidłowe źródło kursu.");
            Check(rate.RateDate is not null, "exchangeRates.rateDate", "Podaj datę kursu.");
            Text(rate.SourceReference, 256, "exchangeRates.sourceReference"); Text(rate.SelectionRule, 256, "exchangeRates.selectionRule"); Text(rate.OverrideReason, 1000, "exchangeRates.overrideReason");
            Check(!rate.IsManualOverride || !string.IsNullOrWhiteSpace(rate.OverrideReason), "exchangeRates.overrideReason", "Podaj przyczynę ręcznej zmiany kursu.");
        }
        Check(draft.Currency?.Trim().ToUpperInvariant() != "PLN" || draft.ExchangeRates?.Count == 0, "exchangeRates", "Faktura PLN nie wymaga kursu.");
        var details = draft.Details;
        Check(details is not null && details.Version == 1, "details", "Nieobsługiwana wersja danych dodatkowych.");
        if (details is not null)
        {
            Check(details.PaymentMethod is null or >= 1 and <= 8, "details.paymentMethod", "Nieprawidłowa forma płatności (8 = inna).");
            Text(details.OtherPaymentDescription, 256, "details.otherPaymentDescription");
            Check(details.PaymentMethod != 8 || !string.IsNullOrWhiteSpace(details.OtherPaymentDescription), "details.otherPaymentDescription", "Podaj opis innej płatności.");
            Check(details.PaymentTerms is not null && details.PaymentTerms.Count <= 100, "details.paymentTerms", "Maksymalnie 100 terminów.");
            foreach (var term in details.PaymentTerms ?? [])
            {
                if (term is null) { Check(false, "details.paymentTerms", "Termin nie może być null."); continue; }
                Check(term.Quantity is null or > 0, "details.paymentTerms.quantity", "Ilość musi być dodatnia.");
                Text(term.Unit, 50, "details.paymentTerms.unit"); Text(term.StartingEvent, 256, "details.paymentTerms.startingEvent");
            }
            Check(details.BankAccounts is not null && details.BankAccounts.Count <= 100, "details.bankAccounts", "Maksymalnie 100 rachunków.");
            foreach (var account in details.BankAccounts ?? [])
            {
                if (account is null) { Check(false, "details.bankAccounts", "Rachunek nie może być null."); continue; }
                Check(account.Number is not null && account.Number.Length is >= 10 and <= 34, "details.bankAccounts.number", "Numer rachunku musi zawierać 10–34 znaki.");
                Text(account.Swift, 11, "details.bankAccounts.swift"); Text(account.BankName, 256, "details.bankAccounts.bankName"); Text(account.Description, 256, "details.bankAccounts.description");
            }
            Check(details.AdditionalDescriptions is not null && details.AdditionalDescriptions.Count <= 10000, "details.additionalDescriptions", "Maksymalnie 10000 opisów.");
            foreach (var description in details.AdditionalDescriptions ?? [])
            {
                if (description is null) { Check(false, "details.additionalDescriptions", "Opis nie może być null."); continue; }
                Text(description.Key, 256, "details.additionalDescriptions.key"); Text(description.Value, 256, "details.additionalDescriptions.value");
                Check(description.LineId is null || ids.Contains(description.LineId.Value), "details.additionalDescriptions.lineId", "Nieprawidłowe powiązanie pozycji.");
            }
        }
        if (issues.Count != 0) throw new SalesInvoiceValidationException(issues);
    }
}
