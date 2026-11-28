using System.Text;
using System.Text.RegularExpressions;
using System.Xml;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Domain.SalesInvoices;

public static class SalesInvoicePaymentValidator
{
    // Exact LinkDoPlatnosci pattern from FA(3), with the XML entity &amp; decoded.
    private const string LinkPattern = @"\A(https?)://([a-zA-Z0-9][a-zA-Z0-9-]*\.)+[a-zA-Z]{2,}(:[0-9]{1,5})?(/[^\s?#]*)?\?([^#\s]*&)?IPKSeF=(?<id>[0-9]{3}[a-zA-Z0-9]{10})(&[^#\s]*)?(#.*)?$";

    public static void Validate(SalesInvoiceDraft draft, ICollection<InvoiceValidationIssue> issues, bool forIssuance = false)
    {
        var details = draft.Details;
        if (details is null) return; // The aggregate validator reports the missing details object.
        void Error(string path, string code, string text) => issues.Add(new(path, code, text));
        void Token(string? value, int max, string path, bool required = false)
        {
            if (value is null) { if (forIssuance && required) Error(path, "PaymentFieldRequired", "Uzupełnij wymagane pole płatności."); return; }
            var length = Collapse(value).EnumerateRunes().Count();
            if (length > max || forIssuance && length == 0) Error(path, "PaymentTextInvalid", $"Pole płatności wymaga 1–{max} znaków po normalizacji białych znaków XML.");
            try { XmlConvert.VerifyXmlChars(value); }
            catch (XmlException) { Error(path, "InvalidXmlCharacters", "Pole płatności zawiera znaki niedozwolone w XML."); }
        }
        void Date(DateOnly? date, string path, bool actualPayment = false)
        {
            if (date is { } d && (d < new DateOnly(2016, 7, 1) || d > new DateOnly(2050, 1, 1))) Error(path, "InvalidPaymentDate", "Data płatności jest poza zakresem FA(3).");
            if (forIssuance && actualPayment && date > draft.PlannedIssueDate) Error(path, "PaymentAfterIssueDate", "Zapłata wykazana na fakturze musi być dokonana najpóźniej w dniu jej wystawienia.");
        }
        void Method(int? method, string? description, string path)
        {
            if (method is not (null or >= 1 and <= 8)) Error(path + ".paymentMethod", "PaymentMethodInvalid", "Forma płatności: 1–7 według FA(3) lub 8 jako aplikacyjne oznaczenie innej formy.");
            Token(description, 256, path + ".otherPaymentDescription", method == 8);
            if (forIssuance && description is not null && method != 8) Error(path + ".otherPaymentDescription", "PaymentMethodConflict", "Opis innej formy wymaga wyboru innej formy płatności (8).");
        }
        Method(details.PaymentMethod, details.OtherPaymentDescription, "details");
        if (details.PaymentStatus is { } status && !Enum.IsDefined(status)) Error("details.paymentStatus", "PaymentStatusInvalid", "Nieprawidłowa deklaracja zapłaty.");
        Date(details.PaymentDate, "details.paymentDate", true);
        if (details.PartialPayments is null || details.PartialPayments.Count > 100) Error("details.partialPayments", "PartialPaymentsInvalid", "Podaj kolekcję zawierającą maksymalnie 100 wpłat.");
        for (var i = 0; i < (details.PartialPayments?.Count ?? 0); i++)
        {
            var payment = details.PartialPayments![i]; var path = $"details.partialPayments[{i}]";
            if (payment is null) { Error(path, "PartialPaymentRequired", "Wpłata nie może być null."); continue; }
            if (payment.Amount is { } amount && (amount < -FinancialMath.MaxAmount || amount > FinancialMath.MaxAmount || !FinancialMath.HasScale(amount, 2))) Error(path + ".amount", "PartialPaymentAmountInvalid", "Kwota wpłaty musi odpowiadać TKwotowy FA(3), z dokładnością do dwóch miejsc po przecinku.");
            Date(payment.PaymentDate, path + ".paymentDate", true); Method(payment.PaymentMethod, payment.OtherPaymentDescription, path);
            if (forIssuance && payment.Amount is null) Error(path + ".amount", "PartialPaymentAmountRequired", "Podaj kwotę wpłaty.");
            if (forIssuance && payment.PaymentDate is null) Error(path + ".paymentDate", "PartialPaymentDateRequired", "Podaj datę wpłaty.");
        }
        if (forIssuance)
        {
            if (details.PaymentStatus == InvoicePaymentStatus.PaidOnce)
            {
                if (details.PaymentDate is null) Error("details.paymentDate", "PaymentDateRequired", "Deklaracja zapłaty jednorazowej wymaga daty zapłaty.");
                if (details.PartialPayments is { Count: > 0 }) Error("details.partialPayments", "PaymentChoiceConflict", "Zapłata jednorazowa i zapłaty w częściach są alternatywnymi gałęziami FA(3).");
            }
            else
            {
                if (details.PaymentDate is not null) Error("details.paymentDate", "PaymentChoiceConflict", "Wspólna data zapłaty dotyczy wyłącznie zapłaty jednorazowej.");
                if (details.PaymentStatus is InvoicePaymentStatus.PartiallyPaid or InvoicePaymentStatus.FullyPaidInParts && details.PartialPayments is not { Count: > 0 }) Error("details.partialPayments", "PartialPaymentsRequired", "Wybrana deklaracja wymaga co najmniej jednej wpłaty.");
                if (details.PaymentStatus == InvoicePaymentStatus.FullyPaidInParts && details.PartialPayments is { Count: 1 }) Error("details.partialPayments", "FinalPaymentPartsRequired", "Według MF pełna zapłata w częściach wymaga co najmniej dwóch wpłat.");
                if (details.PaymentStatus is null && details.PartialPayments is { Count: > 0 }) Error("details.paymentStatus", "PaymentStatusRequired", "Wybierz deklarację zapłaty w częściach.");
            }
        }
        if (details.PaymentTerms is null || details.PaymentTerms.Count > 100) Error("details.paymentTerms", "PaymentTermsInvalid", "Maksymalnie 100 terminów płatności.");
        for (var i = 0; i < (details.PaymentTerms?.Count ?? 0); i++)
        {
            var term = details.PaymentTerms![i]; var path = $"details.paymentTerms[{i}]";
            if (term is null) { Error(path, "PaymentTermRequired", "Termin płatności nie może być null."); continue; }
            Date(term.DueDate, path + ".dueDate");
            var hasDescription = term.Quantity is not null || term.Unit is not null || term.StartingEvent is not null;
            Token(term.Unit, 50, path + ".unit"); Token(term.StartingEvent, 256, path + ".startingEvent");
            if (forIssuance && hasDescription && (term.Quantity is null || term.Unit is null || Collapse(term.Unit).Length == 0 || term.StartingEvent is null || Collapse(term.StartingEvent).Length == 0)) Error(path, "PaymentTermDescriptionIncomplete", "Opis terminu wymaga ilości, jednostki i zdarzenia początkowego.");
        }
        if (details.CashDiscount is { } discount)
        {
            Token(discount.Conditions, 256, "details.cashDiscount.conditions", true); Token(discount.AmountDescription, 256, "details.cashDiscount.amountDescription", true);
        }
        Token(details.PaymentLink, 512, "details.paymentLink"); Token(details.KsefPaymentId, 13, "details.ksefPaymentId");
        Match? link = null;
        if (details.PaymentLink is { } rawLink && (forIssuance || rawLink.Length > 0))
        {
            link = Regex.Match(Collapse(rawLink), LinkPattern, RegexOptions.CultureInvariant);
            if (!link.Success) Error("details.paymentLink", "PaymentLinkInvalid", "Link musi odpowiadać wzorcowi FA(3) i zawierać parametr IPKSeF.");
        }
        if (details.KsefPaymentId is { } id && (forIssuance || id.Length > 0) && !Regex.IsMatch(Collapse(id), @"\A[0-9]{3}[a-zA-Z0-9]{10}$", RegexOptions.CultureInvariant)) Error("details.ksefPaymentId", "KsefPaymentIdInvalid", "IPKSeF wymaga trzech cyfr oraz dziesięciu znaków alfanumerycznych.");
        if (forIssuance && link is { Success: true } && details.KsefPaymentId is { } explicitId && link.Groups["id"].Value != Collapse(explicitId)) Error("details.ksefPaymentId", "PaymentIdentifierMismatch", "Identyfikator płatności jest różny od identyfikatora zawartego w linku.");
        // No balance calculation, paid-amount comparison or automatic deduction from Rozliczenie.
    }

    private static string Collapse(string value) => Regex.Replace(value, "[ \\t\\r\\n]+", " ").Trim(' ');
}
