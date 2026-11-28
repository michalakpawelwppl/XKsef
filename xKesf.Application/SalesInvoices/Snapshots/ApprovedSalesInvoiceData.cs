using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace xKsef.Application.SalesInvoices.Snapshots;

[JsonConverter(typeof(SnapshotDecimalConverter))]
public readonly record struct SnapshotDecimal(decimal Value)
{
    public static implicit operator SnapshotDecimal(decimal value) => new(value);
}

public sealed class SnapshotDecimalConverter : JsonConverter<SnapshotDecimal>
{
    public override SnapshotDecimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType != JsonTokenType.String || !decimal.TryParse(reader.GetString(), NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var value))
            throw new JsonException("Kwota musi być ciągiem dziesiętnym z kropką, bez notacji wykładniczej.");
        // decimal.TryParse can silently round a longer fractional part. Reject any lossy input.
        var raw = reader.GetString()!;
        var point = raw.IndexOf('.');
        if (point >= 0 && raw.Length - point - 1 > 28) throw new JsonException("Zbyt duża skala wartości decimal.");
        var normalized = raw.TrimStart('+');
        var parts = normalized.Split('.');
        var canonicalInput = parts[0].TrimStart('0');
        if (canonicalInput == "" || canonicalInput == "-") canonicalInput += "0";
        if (parts.Length == 2 && parts[1].TrimEnd('0').Length > 0) canonicalInput += "." + parts[1].TrimEnd('0');
        var canonicalValue = value.ToString("0.############################", CultureInfo.InvariantCulture);
        if (canonicalInput != canonicalValue && !(value == 0 && normalized.TrimStart('-').All(c => c is '0' or '.')))
            throw new JsonException("Wartość nie mieści się dokładnie w decimal.");
        return new(value);
    }
    public override void Write(Utf8JsonWriter writer, SnapshotDecimal value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
}

// Nullable values preserve unknown historical data; collections are read-only on deserialization.
public sealed record ApprovedDiscount(int? Kind, SnapshotDecimal? Value);
public sealed record ApprovedInvoiceAddress(string? CountryCode, string? Line1, string? Line2 = null, string? Gln = null);
public sealed record ApprovedInvoiceContact(string? Email, string? Phone);
public sealed record ApprovedInvoiceParty
{
    public string? BuyerLinkKey { get; init; }
    public string? CustomerNumber { get; init; }
    public int? SellerStatus { get; init; }
    public int? TaxpayerStatus { get; init; }
    public ApprovedQualificationDecision? PlPrefix { get; init; }
    public string? Name { get; init; }
    public int? IdentifierKind { get; init; }
    public string? Identifier { get; init; }
    public string? IdentifierCountryCode { get; init; }
    public ApprovedInvoiceAddress? Address { get; init; }
    public ApprovedInvoiceAddress? CorrespondenceAddress { get; init; }
    public IReadOnlyList<ApprovedInvoiceContact>? Contacts { get; init; }
    public Guid? SourceContractorId { get; init; }
    public string? Eori { get; init; }
}
public sealed record ApprovedSalesInvoiceLine
{
    public int? Ordinal { get; init; }
    public DateOnly? SaleDate { get; init; }
    public ApprovedTaxTreatment? TaxTreatment { get; init; }
    public ApprovedExemptionBasis? ExemptionBasis { get; init; }
    public int? Procedure { get; init; }
    public string? Pkob { get; init; }
    public ApprovedQualificationDecision? Annex15 { get; init; }
    public SnapshotDecimal? ExciseAmount { get; init; }
    public Guid? Id { get; init; }
    public string? Description { get; init; }
    public string? Unit { get; init; }
    public SnapshotDecimal? Quantity { get; init; }
    public SnapshotDecimal? UnitPrice { get; init; }
    public int? PriceBasis { get; init; }
    public string? TaxCode { get; init; }
    public ApprovedDiscount? Discount { get; init; }
    public string? ProductCode { get; init; }
    public string? Gtin { get; init; }
    public string? Pkwiu { get; init; }
    public string? Cn { get; init; }
    public string? Gtu { get; init; }
}
public sealed record ApprovedExchangeRate
{
    public Guid? Id { get; init; }
    public string? FromCurrency { get; init; }
    public string? ToCurrency { get; init; }
    public SnapshotDecimal? Rate { get; init; }
    public SnapshotDecimal? QuotedUnits { get; init; }
    public SnapshotDecimal? OriginalRate { get; init; }
    public int? Source { get; init; }
    public string? SourceReference { get; init; }
    public DateOnly? RateDate { get; init; }
    public DateOnly? ReferenceDate { get; init; }
    public string? SelectionRule { get; init; }
    public DateTimeOffset? RetrievedAt { get; init; }
    public bool? IsManualOverride { get; init; }
    public string? OverrideReason { get; init; }
    public Guid? OriginalSnapshotId { get; init; }
}
public sealed record ApprovedPaymentTerm(DateOnly? DueDate, int? Quantity = null, string? Unit = null, string? StartingEvent = null);
public sealed record ApprovedBankAccount(string? Number, string? Swift = null, string? BankName = null, string? Description = null, int? BankOwnAccountType = null);
public sealed record ApprovedAdditionalDescription(string? Key, string? Value, Guid? LineId = null);
public sealed record ApprovedSalesInvoiceDetails
{
    public int? Version { get; init; }
    public int? PaymentMethod { get; init; }
    public string? OtherPaymentDescription { get; init; }
    public IReadOnlyList<ApprovedPaymentTerm>? PaymentTerms { get; init; }
    public IReadOnlyList<ApprovedBankAccount>? BankAccounts { get; init; }
    public IReadOnlyList<ApprovedBankAccount>? FactorBankAccounts { get; init; }
    public IReadOnlyList<ApprovedAdditionalDescription>? AdditionalDescriptions { get; init; }
}
public sealed record ApprovedSalesInvoiceDraft
{
    public int? ModelVersion { get; init; }
    public string? IssuePlace { get; init; }
    public int? SaleDateMode { get; init; }
    public ApprovedSalePeriod? SalePeriod { get; init; }
    public IReadOnlyList<ApprovedParticipant>? Participants { get; init; }
    public ApprovedFa3Data? Fa3 { get; init; }
    public Guid? NumberSeriesId { get; init; }
    public int? Kind { get; init; }
    public DateOnly? PlannedIssueDate { get; init; }
    public DateOnly? SaleDate { get; init; }
    public string? Currency { get; init; }
    public int? VatMethod { get; init; }
    public ApprovedDiscount? GlobalDiscount { get; init; }
    public int? GlobalDiscountBasis { get; init; }
    public int? PdfLanguage { get; init; }
    public string? Notes { get; init; }
    public ApprovedInvoiceParty? Seller { get; init; }
    public ApprovedInvoiceParty? Buyer { get; init; }
    public IReadOnlyList<ApprovedSalesInvoiceLine>? Lines { get; init; }
    public IReadOnlyList<ApprovedExchangeRate>? ExchangeRates { get; init; }
    public ApprovedSalesInvoiceDetails? Details { get; init; }
}
public sealed record ApprovedSalesInvoiceIssue(string? Path, string? Code, string? Message);
public sealed record ApprovedSalesInvoiceTotals(SnapshotDecimal? Net, SnapshotDecimal? Vat, SnapshotDecimal? Gross, SnapshotDecimal? VatPln, SnapshotDecimal? GlobalDiscountAmount);
public sealed record ApprovedSalesInvoiceLineResult(Guid? Id, SnapshotDecimal? Net, SnapshotDecimal? Vat, SnapshotDecimal? Gross, SnapshotDecimal? VatPln, SnapshotDecimal? GlobalDiscountAllocation, SnapshotDecimal? VatRoundingAdjustment,
    int? VatAmountOrigin = null, SnapshotDecimal? VatBaseRoundingAdjustment = null, SnapshotDecimal? LineDiscount = null);
public sealed record ApprovedVatCalculationDetails(int? Version, int? Method, int? Basis, SnapshotDecimal? TaxBase, SnapshotDecimal? Rate);
public sealed record ApprovedSalesInvoiceTaxSummary(string? TaxCode, SnapshotDecimal? Net, SnapshotDecimal? Vat, SnapshotDecimal? Gross, SnapshotDecimal? VatPln, ApprovedVatCalculationDetails? CalculationDetails = null);
public sealed record ApprovedExchangeRateHistory(ApprovedExchangeRate Snapshot, bool? IsCurrent, DateTimeOffset? CreatedAt, string? CreatedBy);
