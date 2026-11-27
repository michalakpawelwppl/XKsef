using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace xKsef.Contracts.SalesInvoices;

[JsonConverter(typeof(ExactDecimalConverter))]
public readonly record struct ExactDecimal(decimal Value)
{
    public static implicit operator ExactDecimal(decimal value) => new(value);
}

public sealed class ExactDecimalConverter : JsonConverter<ExactDecimal>
{
    public override ExactDecimal Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
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
    public override void Write(Utf8JsonWriter writer, ExactDecimal value, JsonSerializerOptions options) => writer.WriteStringValue(value.Value.ToString(CultureInfo.InvariantCulture));
}

public sealed record DiscountDto(int Kind, ExactDecimal Value);
public sealed record InvoiceAddressDto(string CountryCode, string Line1, string? Line2 = null, string? Gln = null);
public sealed record InvoiceContactDto(string? Email, string? Phone);
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record InvoicePartyDto
{
    public string? Name { get; init; }
    public int IdentifierKind { get; init; }
    public string? Identifier { get; init; }
    public string? IdentifierCountryCode { get; init; }
    public InvoiceAddressDto? Address { get; init; }
    public InvoiceAddressDto? CorrespondenceAddress { get; init; }
    public IReadOnlyList<InvoiceContactDto> Contacts { get; init; } = [];
    public Guid? SourceContractorId { get; init; }
    public string? Eori { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalesInvoiceLineDto
{
    public Guid Id { get; init; }
    public string? Description { get; init; }
    public string? Unit { get; init; }
    public ExactDecimal? Quantity { get; init; }
    public ExactDecimal? UnitPrice { get; init; }
    public int PriceBasis { get; init; }
    public string TaxCode { get; init; } = "23";
    public DiscountDto? Discount { get; init; }
    public string? ProductCode { get; init; }
    public string? Gtin { get; init; }
    public string? Pkwiu { get; init; }
    public string? Cn { get; init; }
    public string? Gtu { get; init; }
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record ExchangeRateDto
{
    public Guid Id { get; init; }
    public string FromCurrency { get; init; } = "EUR";
    public string ToCurrency { get; init; } = "PLN";
    public ExactDecimal Rate { get; init; }
    public ExactDecimal QuotedUnits { get; init; } = new(1m);
    public ExactDecimal? OriginalRate { get; init; }
    public int Source { get; init; }
    public string? SourceReference { get; init; }
    public DateOnly? RateDate { get; init; }
    public DateOnly? ReferenceDate { get; init; }
    public string? SelectionRule { get; init; }
    public DateTimeOffset? RetrievedAt { get; init; }
    public bool IsManualOverride { get; init; }
    public string? OverrideReason { get; init; }
    public Guid? OriginalSnapshotId { get; init; }
}
public sealed record PaymentTermDto(DateOnly? DueDate, int? Quantity = null, string? Unit = null, string? StartingEvent = null);
public sealed record BankAccountDto(string Number, string? Swift = null, string? BankName = null, string? Description = null);
public sealed record AdditionalDescriptionDto(string Key, string Value, Guid? LineId = null);
public sealed record SalesInvoiceDetailsDto
{
    public int Version { get; init; } = 1;
    public int? PaymentMethod { get; init; }
    public string? OtherPaymentDescription { get; init; }
    public IReadOnlyList<PaymentTermDto> PaymentTerms { get; init; } = [];
    public IReadOnlyList<BankAccountDto> BankAccounts { get; init; } = [];
    public IReadOnlyList<AdditionalDescriptionDto> AdditionalDescriptions { get; init; } = [];
}
[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed record SalesInvoiceDraftDto
{
    public int Kind { get; init; }
    public DateOnly? PlannedIssueDate { get; init; }
    public DateOnly? SaleDate { get; init; }
    public string Currency { get; init; } = "PLN";
    public int VatMethod { get; init; }
    public DiscountDto? GlobalDiscount { get; init; }
    public int GlobalDiscountBasis { get; init; }
    public int PdfLanguage { get; init; }
    public string? Notes { get; init; }
    public InvoicePartyDto? Seller { get; init; }
    public InvoicePartyDto? Buyer { get; init; }
    public IReadOnlyList<SalesInvoiceLineDto> Lines { get; init; } = [];
    public IReadOnlyList<ExchangeRateDto> ExchangeRates { get; init; } = [];
    public SalesInvoiceDetailsDto Details { get; init; } = new();
}
public sealed record UpdateSalesInvoiceRequest(string Version, SalesInvoiceDraftDto Draft);
public sealed record RecalculateSalesInvoiceRequest(string Version);
public sealed record SalesInvoiceIssueDto(string Path, string Code, string Message);
public sealed record SalesInvoiceTotalsDto(ExactDecimal? Net, ExactDecimal? Vat, ExactDecimal? Gross, ExactDecimal? VatPln, ExactDecimal GlobalDiscountAmount);
public sealed record SalesInvoiceLineResultDto(Guid Id, ExactDecimal? Net, ExactDecimal? Vat, ExactDecimal? Gross, ExactDecimal? VatPln, ExactDecimal GlobalDiscountAllocation, ExactDecimal VatRoundingAdjustment);
public sealed record SalesInvoiceTaxSummaryDto(string TaxCode, ExactDecimal Net, ExactDecimal Vat, ExactDecimal Gross, ExactDecimal VatPln);
public sealed record ExchangeRateHistoryDto(ExchangeRateDto Snapshot, bool IsCurrent, DateTimeOffset CreatedAt, string CreatedBy);
public sealed record SalesInvoiceDto(Guid Id, int Status, int Revision, string Version, SalesInvoiceDraftDto Draft, bool CalculationComplete, string CalculationVersion, SalesInvoiceTotalsDto Totals, IReadOnlyList<SalesInvoiceLineResultDto> LineResults, IReadOnlyList<SalesInvoiceTaxSummaryDto> TaxSummaries, IReadOnlyList<ExchangeRateHistoryDto> ExchangeRateHistory, IReadOnlyList<SalesInvoiceIssueDto> MissingData, DateTimeOffset CreatedAt, DateTimeOffset UpdatedAt);
public sealed record SalesInvoiceListItemDto(Guid Id, int Status, DateOnly? PlannedIssueDate, string Currency, ExactDecimal? Net, ExactDecimal? Vat, ExactDecimal? Gross, DateTimeOffset UpdatedAt, int Revision);
public sealed record SalesInvoiceListDto(IReadOnlyList<SalesInvoiceListItemDto> Items, int TotalCount, int Page, int PageSize);
