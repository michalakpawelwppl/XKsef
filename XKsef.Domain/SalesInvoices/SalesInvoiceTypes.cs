namespace xKsef.Domain.SalesInvoices;

public enum SalesInvoiceStatus { Draft = 0, Approved = 1, Processing = 2, Issued = 3, Rejected = 4, Cancelled = 5 }
public enum SalesInvoiceKind { Vat = 0, Advance = 1, Final = 2, Correction = 3, AdvanceCorrection = 4, FinalCorrection = 5, Simplified = 6 }
public enum VatCalculationMethod { PerLineNet = 0, PerLineGross = 1, ByNetTotals = 2, ByGrossTotals = 3 }
public enum PriceBasis { Net = 0, Gross = 1 }
public enum DiscountKind { Percent = 0, Amount = 1 }
public enum PdfPresentationLanguage { Pl = 0, En = 1, PlEn = 2 }
public enum PartyRole { Seller = 0, Buyer = 1 }
public enum TaxIdentifierKind { Nip = 0, EuVat = 1, Other = 2, None = 3, InternalId = 4 }
public enum ExchangeRateSource { Manual = 0, Nbp = 1, Ecb = 2 }
public enum InvoiceReferenceKind { Correction = 0, Advance = 1 }

public sealed record InvoiceDiscount(DiscountKind Kind, decimal Value);
public sealed record InvoiceAddress(string CountryCode, string Line1, string? Line2 = null, string? Gln = null);
public sealed record InvoiceContact(string? Email, string? Phone);
public sealed record SalesInvoicePartyData
{
    public string? Name { get; init; }
    public TaxIdentifierKind IdentifierKind { get; init; }
    public string? Identifier { get; init; }
    public string? IdentifierCountryCode { get; init; }
    public InvoiceAddress? Address { get; init; }
    public InvoiceAddress? CorrespondenceAddress { get; init; }
    public IReadOnlyList<InvoiceContact> Contacts { get; init; } = [];
    public Guid? SourceContractorId { get; init; }
    public string? Eori { get; init; }
    public string? BuyerLinkKey { get; init; }
    public string? CustomerNumber { get; init; }
    public SellerInformationStatus? SellerStatus { get; init; }
    public QualificationValue TaxpayerStatus { get; init; }
    public QualificationDecisionData PlPrefix { get; init; } = new();
    internal SalesInvoicePartyData Freeze() => this with { Contacts = Array.AsReadOnly(Contacts.ToArray()) };
}

public sealed record SalesInvoiceLineInput
{
    public Guid Id { get; init; }
    public string? Description { get; init; }
    public string? Unit { get; init; }
    public decimal? Quantity { get; init; }
    public decimal? UnitPrice { get; init; }
    public PriceBasis PriceBasis { get; init; }
    public string TaxCode { get; init; } = "23";
    public InvoiceDiscount? Discount { get; init; }
    public string? ProductCode { get; init; }
    public string? Gtin { get; init; }
    public string? Pkwiu { get; init; }
    public string? Cn { get; init; }
    public string? Gtu { get; init; }
    public DateOnly? SaleDate { get; init; }
    public InvoiceTaxTreatmentData? TaxTreatment { get; init; }
    public InvoiceExemptionBasisData? ExemptionBasis { get; init; }
    public InvoiceProcedureCode? Procedure { get; init; }
    public string? Pkob { get; init; }
    public QualificationDecisionData Annex15 { get; init; } = new();
    public decimal? ExciseAmount { get; init; }
}

public sealed record ExchangeRateData
{
    public Guid Id { get; init; }
    public string FromCurrency { get; init; } = "EUR";
    public string ToCurrency { get; init; } = "PLN";
    public decimal Rate { get; init; }
    public decimal QuotedUnits { get; init; } = 1m;
    public decimal? OriginalRate { get; init; }
    public ExchangeRateSource Source { get; init; }
    public string? SourceReference { get; init; }
    public DateOnly? RateDate { get; init; }
    public DateOnly? ReferenceDate { get; init; }
    public string? SelectionRule { get; init; }
    public DateTimeOffset? RetrievedAt { get; init; }
    public bool IsManualOverride { get; init; }
    public string? OverrideReason { get; init; }
    public Guid? OriginalSnapshotId { get; init; }
}

public sealed record InvoicePaymentTerm(DateOnly? DueDate, int? Quantity = null, string? Unit = null, string? StartingEvent = null);
// FA(3) TRachunekWlasnyBanku; omission is distinct from each of its three meanings.
public enum BankOwnAccountType { PurchasedReceivables = 1, CollectionsForSupplier = 2, OwnOperationsNonSettlement = 3 }
public sealed record InvoiceBankAccount(string Number, string? Swift = null, string? BankName = null, string? Description = null, BankOwnAccountType? BankOwnAccountType = null);
public sealed record InvoiceAdditionalDescription(string Key, string Value, Guid? LineId = null);
public sealed record SalesInvoiceDetailsData
{
    public InvoiceSettlementData? Settlement { get; init; }
    public int Version { get; init; } = 1;
    public int? PaymentMethod { get; init; }
    public string? OtherPaymentDescription { get; init; }
    public IReadOnlyList<InvoicePaymentTerm> PaymentTerms { get; init; } = [];
    public IReadOnlyList<InvoiceBankAccount> BankAccounts { get; init; } = [];
    public IReadOnlyList<InvoiceBankAccount> FactorBankAccounts { get; init; } = [];
    public IReadOnlyList<InvoiceAdditionalDescription> AdditionalDescriptions { get; init; } = [];
}

public sealed record SalesInvoiceDraft
{
    public int ModelVersion { get; init; } = 1;
    public string? IssuePlace { get; init; }
    public SaleDateMode? SaleDateMode { get; init; }
    public InvoiceSalePeriodData? SalePeriod { get; init; }
    public IReadOnlyList<InvoiceParticipantData> Participants { get; init; } = [];
    public SalesInvoiceFa3Data Fa3 { get; init; } = new();
    public Guid? NumberSeriesId { get; init; }
    public SalesInvoiceKind Kind { get; init; }
    public DateOnly? PlannedIssueDate { get; init; }
    public DateOnly? SaleDate { get; init; }
    public string Currency { get; init; } = "PLN";
    public VatCalculationMethod VatMethod { get; init; }
    public InvoiceDiscount? GlobalDiscount { get; init; }
    public PriceBasis GlobalDiscountBasis { get; init; }
    public PdfPresentationLanguage PdfLanguage { get; init; }
    public string? Notes { get; init; }
    public SalesInvoicePartyData? Seller { get; init; }
    public SalesInvoicePartyData? Buyer { get; init; }
    public IReadOnlyList<SalesInvoiceLineInput> Lines { get; init; } = [];
    public IReadOnlyList<ExchangeRateData> ExchangeRates { get; init; } = [];
    public SalesInvoiceDetailsData Details { get; init; } = new();

    public SalesInvoiceDraft Normalize() => this with
    {
        Currency = (Currency ?? "").Trim().ToUpperInvariant(),
        ModelVersion = SalesInvoiceFa3Model.HasExtendedData(this) ? Math.Max(ModelVersion, 2) : ModelVersion
    };
}

public sealed record InvoiceValidationIssue(string Path, string Code, string Message);
public sealed class SalesInvoiceValidationException(IReadOnlyList<InvoiceValidationIssue> issues) : Exception("Nieprawidłowe dane projektu faktury.")
{
    public IReadOnlyList<InvoiceValidationIssue> Issues { get; } = issues;
}
public sealed class SalesInvoiceConflictException() : Exception("Projekt został zmieniony lub nie jest już w stanie Draft.");
