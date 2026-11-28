using System.Text;
using System.Text.RegularExpressions;
using System.Xml;

namespace xKsef.Domain.SalesInvoices;

// FA(3) TRachunekBankowy, TNrRB, SWIFT_Type and TZnakowy. No IBAN/NRB checksum requirement in XSD.
public static class InvoiceBankAccountValidator
{
    public static void Validate(IReadOnlyList<InvoiceBankAccount>? accounts, int maximum, string path, ICollection<InvoiceValidationIssue> issues)
    {
        void Error(string field, string code, string message) => issues.Add(new(field, code, message));
        if (accounts is null || accounts.Count > maximum)
            Error(path, "BankAccountCollectionInvalid", $"Podaj kolekcję zawierającą maksymalnie {maximum} rachunków.");
        if (accounts is null) return;
        for (var i = 0; i < accounts.Count; i++)
        {
            var account = accounts[i]; var field = $"{path}[{i}]";
            if (account is null) { Error(field, "BankAccountRequired", "Rachunek nie może być null."); continue; }
            if (account.Number is null || account.Number.EnumerateRunes().Count() is < 10 or > 34)
                Error(field + ".number", "BankAccountNumberInvalid", "Pełny numer rachunku musi zawierać 10–34 znaki zgodnie z FA(3).");
            // Match the .NET XSD validator's pattern semantics, including its treatment of a final LF.
            if (account.Swift is not null && !Regex.IsMatch(account.Swift, @"\A[A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?$", RegexOptions.CultureInvariant))
                Error(field + ".swift", "BankAccountSwiftInvalid", "Kod SWIFT musi odpowiadać wzorcowi FA(3): [A-Z]{6}[A-Z0-9]{2}([A-Z0-9]{3})?.");
            if (account.BankOwnAccountType is { } type && !Enum.IsDefined(type))
                Error(field + ".bankOwnAccountType", "BankOwnAccountTypeInvalid", "Typ rachunku własnego banku lub SKOK może mieć wartość 1, 2 albo 3; można go pominąć.");
            Token(account.BankName, field + ".bankName"); Token(account.Description, field + ".description");
            XmlText(account.Number, field + ".number"); XmlText(account.Swift, field + ".swift");
        }

        void Token(string? value, string field)
        {
            if (value is null) return;
            // xsd:token collapses XML whitespace for validation; preserve the user's original input in storage.
            var collapsed = Regex.Replace(value, "[ \\t\\r\\n]+", " ").Trim(' ');
            if (collapsed.EnumerateRunes().Count() is < 1 or > 256)
                Error(field, "BankAccountTextInvalid", "Pole rachunku musi zawierać 1–256 znaków po normalizacji białych znaków XML.");
            XmlText(value, field);
        }
        void XmlText(string? value, string field)
        {
            if (value is null) return;
            try { XmlConvert.VerifyXmlChars(value); }
            catch (XmlException) { Error(field, "InvalidXmlCharacters", "Pole rachunku zawiera znaki niedozwolone w XML."); }
        }
    }
}
