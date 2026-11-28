using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Domain.SalesInvoices;

public sealed class SalesInvoice
{
    private readonly List<SalesInvoiceLine> _lines = [];
    private readonly List<SalesInvoicePartySnapshot> _parties = [];
    private readonly List<SalesInvoiceTaxSummary> _taxSummaries = [];
    private readonly List<SalesInvoiceExchangeRateSnapshot> _exchangeRates = [];
    private readonly List<SalesInvoiceReference> _references = [];
    private readonly List<SalesInvoiceApproval> _approvals = [];
    private readonly List<SalesInvoiceParticipant> _participants = [];
    private SalesInvoice() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public int ModelVersion { get; private set; } = 1;
    public string? IssuePlace { get; private set; }
    public SaleDateMode? SaleDateMode { get; private set; }
    public InvoiceSalePeriodData? SalePeriod { get; private set; }
    public SalesInvoiceFa3Data Fa3 { get; private set; } = new();
    public IReadOnlyList<SalesInvoiceParticipant> Participants => _participants.AsReadOnly();
    public Guid? NumberSeriesId { get; private set; }
    public Guid? FinalNumberReservationId { get; private set; }
    public string? InvoiceNumber { get; private set; }
    public string? NormalizedInvoiceNumber { get; private set; }
    public string? KsefNumber { get; private set; }
    public SalesInvoiceStatus Status { get; private set; }
    public SalesInvoiceKind Kind { get; private set; }
    public DateOnly? PlannedIssueDate { get; private set; }
    public DateOnly? SaleDate { get; private set; }
    public string Currency { get; private set; } = "PLN";
    public VatCalculationMethod VatMethod { get; private set; }
    public InvoiceDiscount? GlobalDiscount { get; private set; }
    public PriceBasis GlobalDiscountBasis { get; private set; }
    public PdfPresentationLanguage PdfLanguage { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public string CreatedBy { get; private set; } = "";
    public string UpdatedBy { get; private set; } = "";
    public int Revision { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public string CalculationVersion { get; private set; } = InvoiceCalculationResult.AlgorithmVersion;
    public bool CalculationComplete { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal? VatAmount { get; private set; }
    public decimal? GrossAmount { get; private set; }
    public decimal? VatAmountPln { get; private set; }
    public decimal GlobalDiscountAmount { get; private set; }
    public SalesInvoiceDetails Details { get; private set; } = null!;
    public InvoiceSettlementAmounts? SettlementAmounts { get; private set; }
    public IReadOnlyList<SalesInvoiceLine> Lines => _lines.AsReadOnly();
    public IReadOnlyList<SalesInvoicePartySnapshot> Parties => _parties.AsReadOnly();
    public IReadOnlyList<SalesInvoiceTaxSummary> TaxSummaries => _taxSummaries.AsReadOnly();
    public IReadOnlyList<SalesInvoiceExchangeRateSnapshot> ExchangeRates => _exchangeRates.AsReadOnly();
    public IReadOnlyList<SalesInvoiceReference> References => _references.AsReadOnly();
    public IReadOnlyList<SalesInvoiceApproval> Approvals => _approvals.AsReadOnly();

    // The application service validates this exact aggregate before invoking the transition.
    public void Approve(int expectedRevision, string actor, DateTimeOffset now, string snapshotJson, Guid? approvalId = null)
    {
        if (Status != SalesInvoiceStatus.Draft || Revision != expectedRevision) throw new SalesInvoiceConflictException();
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450) throw new ArgumentException("Nieprawidłowy użytkownik.");
        if (string.IsNullOrWhiteSpace(snapshotJson)) throw new ArgumentException("Brak snapshotu zatwierdzanego dokumentu.");
        if (!SalesInvoiceCalculator.Supports(VatMethod)) throw new SalesInvoiceValidationException([new("vatMethod", "VatMethodNotImplemented", "Wybrana metoda VAT nie jest jeszcze obsługiwana.")]);
        if (!CalculationComplete) throw new SalesInvoiceValidationException([new("totals", "CalculationIncomplete", "Nie można zatwierdzić faktury bez kompletnych obliczeń.")]);
        if (approvalId == Guid.Empty || _approvals.Count != 0) throw new SalesInvoiceConflictException();
        var approvedRevision = checked(Revision + 1);
        _approvals.Add(new(TenantId, Id, actor, now, Revision, approvedRevision, snapshotJson, approvalId ?? Guid.NewGuid()));
        Status = SalesInvoiceStatus.Approved; Revision = approvedRevision; UpdatedBy = actor; UpdatedAt = now;
    }

    public void Unapprove(int expectedRevision, string actor, DateTimeOffset now)
    {
        if (Status != SalesInvoiceStatus.Approved || Revision != expectedRevision || InvoiceNumber is not null || KsefNumber is not null || FinalNumberReservationId is not null) throw new SalesInvoiceConflictException();
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450) throw new ArgumentException("Nieprawidłowy użytkownik.");
        // The repository must hold the numbering lock and verify no issuing operation has ever started.
        _approvals.Clear(); Status = SalesInvoiceStatus.Draft; Revision = checked(Revision + 1); UpdatedBy = actor; UpdatedAt = now;
    }

    public static SalesInvoice Create(Guid tenantId, SalesInvoiceDraft draft, string actor, DateTimeOffset now, SalesInvoiceCalculator calculator)
    {
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(actor) || actor.Length > 450) throw new ArgumentException("Nieprawidłowy tenant lub użytkownik.");
        var invoice = new SalesInvoice { Id = Guid.NewGuid(), TenantId = tenantId, CreatedAt = now, CreatedBy = actor };
        invoice.UpdateDraft(draft, actor, now, calculator);
        return invoice;
    }

    public void FinalizeNumber(Guid approvalId, Guid reservationId, string number, string ksefNumber, string actor, DateTimeOffset now)
    {
        if (InvoiceNumber is not null)
        {
            if (FinalNumberReservationId == reservationId && InvoiceNumber == number && KsefNumber == ksefNumber) return;
            throw new SalesInvoiceConflictException();
        }
        if (Status != SalesInvoiceStatus.Approved || _approvals.OrderBy(x => x.ApprovedRevision).LastOrDefault()?.Id != approvalId) throw new SalesInvoiceConflictException();
        if (reservationId == Guid.Empty || string.IsNullOrWhiteSpace(number) || number.Length > 100 || string.IsNullOrWhiteSpace(ksefNumber) || ksefNumber.Length > 100 || string.IsNullOrWhiteSpace(actor) || actor.Length > 450) throw new ArgumentException("Nieprawidłowe dane finalizacji numeru.");
        var revision = checked(Revision + 1);
        FinalNumberReservationId = reservationId; InvoiceNumber = number; NormalizedInvoiceNumber = number.ToUpperInvariant(); KsefNumber = ksefNumber;
        Revision = revision; UpdatedBy = actor; UpdatedAt = now;
        // The issuing process, when implemented, owns the separate transition to Issued.
    }

    public void UpdateDraft(SalesInvoiceDraft draft, string actor, DateTimeOffset now, SalesInvoiceCalculator calculator)
    {
        if (Status != SalesInvoiceStatus.Draft) throw new SalesInvoiceConflictException();
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450) throw new ArgumentException("Nieprawidłowy użytkownik.");
        SalesInvoiceValidator.Validate(draft);
        var previous = Details is null ? null : ToDraft();
        var normalized = SalesInvoiceFa3Audit.Stamp(draft.Normalize(), previous, actor, now);
        var result = calculator.Calculate(normalized); // Validate and calculate before mutating the aggregate.
        var settlement = InvoiceSettlementCalculator.Calculate(normalized.Details.Settlement, result.Gross);
        Kind = normalized.Kind; PlannedIssueDate = normalized.PlannedIssueDate; SaleDate = normalized.SaleDate;
        NumberSeriesId = normalized.NumberSeriesId;
        ModelVersion = normalized.ModelVersion; IssuePlace = normalized.IssuePlace; SaleDateMode = normalized.SaleDateMode; SalePeriod = normalized.SalePeriod;
        Fa3 = normalized.Fa3.Freeze();
        Currency = normalized.Currency; VatMethod = normalized.VatMethod; GlobalDiscount = normalized.GlobalDiscount;
        GlobalDiscountBasis = normalized.GlobalDiscountBasis; PdfLanguage = normalized.PdfLanguage; Notes = normalized.Notes;
        UpdatedAt = now; UpdatedBy = actor; Revision = checked(Revision + 1);
        CalculationVersion = InvoiceCalculationResult.AlgorithmVersion; CalculationComplete = result.IsComplete;
        NetAmount = result.Net; VatAmount = result.Vat; GrossAmount = result.Gross; VatAmountPln = result.VatPln; GlobalDiscountAmount = result.GlobalDiscountAmount;
        SettlementAmounts = settlement;
        var desiredIds = normalized.Lines.Select(x => x.Id).ToHashSet();
        _lines.RemoveAll(x => !desiredIds.Contains(x.Id));
        for (var i = 0; i < normalized.Lines.Count; i++)
        {
            var input = normalized.Lines[i]; var line = _lines.SingleOrDefault(x => x.Id == input.Id);
            if (line is null) { line = new SalesInvoiceLine(input.Id, TenantId, Id); _lines.Add(line); }
            line.Apply(input, i + 1, result.Lines.SingleOrDefault(x => x.Id == input.Id));
        }
        ApplyParty(PartyRole.Seller, normalized.Seller); ApplyParty(PartyRole.Buyer, normalized.Buyer);
        var participantIds = normalized.Participants.Select(x => x.Id).ToHashSet();
        _participants.RemoveAll(x => !participantIds.Contains(x.Id));
        for (var i = 0; i < normalized.Participants.Count; i++)
        {
            var input = normalized.Participants[i]; var participant = _participants.SingleOrDefault(x => x.Id == input.Id);
            if (participant is null) { participant = new(TenantId, Id, input.Id); _participants.Add(participant); }
            participant.Apply(input, i + 1);
        }
        _taxSummaries.Clear();
        _taxSummaries.AddRange(result.TaxSummaries.Select(x => new SalesInvoiceTaxSummary(TenantId, Id, x)));
        var incomingRate = normalized.ExchangeRates.SingleOrDefault();
        var currentRate = _exchangeRates.SingleOrDefault(x => x.IsCurrent);
        if (incomingRate != currentRate?.Data)
        {
            if (currentRate is not null) currentRate.Supersede();
            if (incomingRate is not null)
            {
                // Every changed quotation has its own immutable snapshot, including manual edits in Draft.
                var data = incomingRate with { Id = Guid.NewGuid(), OriginalSnapshotId = currentRate?.Id ?? incomingRate.OriginalSnapshotId };
                _exchangeRates.Add(new SalesInvoiceExchangeRateSnapshot(TenantId, Id, data, actor, now));
            }
        }
        if (Details is null) Details = new SalesInvoiceDetails(TenantId, Id);
        Details.Replace(normalized.Details);
    }

    private void ApplyParty(PartyRole role, SalesInvoicePartyData? data)
    {
        var party = _parties.SingleOrDefault(x => x.Role == role);
        if (data is null) { if (party is not null) _parties.Remove(party); return; }
        if (party is null) { party = new SalesInvoicePartySnapshot(TenantId, Id, role); _parties.Add(party); }
        party.Replace(data);
    }

    public SalesInvoiceDraft ToDraft() => new()
    {
        NumberSeriesId = NumberSeriesId,
        ModelVersion = ModelVersion, IssuePlace = IssuePlace, SaleDateMode = SaleDateMode, SalePeriod = SalePeriod, Fa3 = Fa3.Freeze(),
        Participants = _participants.OrderBy(x => x.Ordinal).Select(x => x.Snapshot()).ToArray(),
        Kind = Kind, PlannedIssueDate = PlannedIssueDate, SaleDate = SaleDate, Currency = Currency, VatMethod = VatMethod,
        GlobalDiscount = GlobalDiscount, GlobalDiscountBasis = GlobalDiscountBasis, PdfLanguage = PdfLanguage, Notes = Notes,
        Seller = _parties.SingleOrDefault(x => x.Role == PartyRole.Seller)?.Snapshot(), Buyer = _parties.SingleOrDefault(x => x.Role == PartyRole.Buyer)?.Snapshot(),
        Lines = _lines.OrderBy(x => x.Ordinal).Select(x => x.ToInput()).ToArray(),
        ExchangeRates = _exchangeRates.Where(x => x.IsCurrent).Select(x => x.Data).ToArray(), Details = Details.Snapshot()
    };
}

public sealed class SalesInvoiceLine
{
    private SalesInvoiceLine() { }
    internal SalesInvoiceLine(Guid id, Guid tenantId, Guid invoiceId) { Id = id; TenantId = tenantId; SalesInvoiceId = invoiceId; }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SalesInvoiceId { get; private set; }
    public int Ordinal { get; private set; }
    public string? Description { get; private set; }
    public string? Unit { get; private set; }
    public decimal? Quantity { get; private set; }
    public decimal? UnitPrice { get; private set; }
    public PriceBasis PriceBasis { get; private set; }
    public string TaxCode { get; private set; } = "23";
    public InvoiceDiscount? Discount { get; private set; }
    public string? ProductCode { get; private set; }
    public string? Gtin { get; private set; }
    public string? Pkwiu { get; private set; }
    public string? Cn { get; private set; }
    public string? Gtu { get; private set; }
    public DateOnly? SaleDate { get; private set; }
    public InvoiceTaxTreatmentData? TaxTreatment { get; private set; }
    public InvoiceExemptionBasisData? ExemptionBasis { get; private set; }
    public InvoiceProcedureCode? Procedure { get; private set; }
    public string? Pkob { get; private set; }
    public QualificationDecisionData Annex15 { get; private set; } = new();
    public decimal? ExciseAmount { get; private set; }
    public decimal? NetAmount { get; private set; }
    public decimal? VatAmount { get; private set; }
    public decimal? GrossAmount { get; private set; }
    public decimal? VatAmountPln { get; private set; }
    public decimal GlobalDiscountAllocation { get; private set; }
    public decimal VatRoundingAdjustment { get; private set; }
    public VatAmountOrigin? VatAmountOrigin { get; private set; }
    public decimal? VatBaseRoundingAdjustment { get; private set; }
    internal void Apply(SalesInvoiceLineInput input, int ordinal, CalculatedInvoiceLine? result)
    {
        Ordinal = ordinal; Description = input.Description; Unit = input.Unit; Quantity = input.Quantity; UnitPrice = input.UnitPrice;
        PriceBasis = input.PriceBasis; TaxCode = input.TaxCode; Discount = input.Discount; ProductCode = input.ProductCode;
        Gtin = input.Gtin; Pkwiu = input.Pkwiu; Cn = input.Cn; Gtu = input.Gtu;
        SaleDate = input.SaleDate; TaxTreatment = input.TaxTreatment; ExemptionBasis = input.ExemptionBasis;
        Procedure = input.Procedure; Pkob = input.Pkob; Annex15 = input.Annex15; ExciseAmount = input.ExciseAmount;
        NetAmount = result?.Net; VatAmount = result?.Vat; GrossAmount = result?.Gross; VatAmountPln = result?.VatPln;
        GlobalDiscountAllocation = result?.GlobalDiscountAllocation ?? 0m; VatRoundingAdjustment = result?.VatRoundingAdjustment ?? 0m;
        VatAmountOrigin = result?.VatAmountOrigin; VatBaseRoundingAdjustment = result?.VatBaseRoundingAdjustment;
    }
    public SalesInvoiceLineInput ToInput() => new() { Id = Id, Description = Description, Unit = Unit, Quantity = Quantity, UnitPrice = UnitPrice, PriceBasis = PriceBasis, TaxCode = TaxCode, Discount = Discount, ProductCode = ProductCode, Gtin = Gtin, Pkwiu = Pkwiu, Cn = Cn, Gtu = Gtu, SaleDate = SaleDate, TaxTreatment = TaxTreatment, ExemptionBasis = ExemptionBasis, Procedure = Procedure, Pkob = Pkob, Annex15 = Annex15, ExciseAmount = ExciseAmount };
}

public sealed class SalesInvoicePartySnapshot
{
    private SalesInvoicePartySnapshot() { }
    internal SalesInvoicePartySnapshot(Guid tenantId, Guid invoiceId, PartyRole role) { Id = Guid.NewGuid(); TenantId = tenantId; SalesInvoiceId = invoiceId; Role = role; }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SalesInvoiceId { get; private set; }
    public PartyRole Role { get; private set; }
    public SalesInvoicePartyData Data { get; private set; } = new();
    internal void Replace(SalesInvoicePartyData data) => Data = data with { Contacts = Array.AsReadOnly(data.Contacts.ToArray()) };
    public SalesInvoicePartyData Snapshot() => Data with { Contacts = Data.Contacts.ToArray() };
}

public sealed class SalesInvoiceTaxSummary
{
    public VatCalculationDetails? CalculationDetails { get; private set; }
    private SalesInvoiceTaxSummary() { }
    internal SalesInvoiceTaxSummary(Guid tenantId, Guid invoiceId, CalculatedTaxSummary result)
    { Id = Guid.NewGuid(); TenantId = tenantId; SalesInvoiceId = invoiceId; TaxCode = result.TaxCode; NetAmount = result.Net; VatAmount = result.Vat; GrossAmount = result.Gross; VatAmountPln = result.VatPln; CalculationDetails = result.CalculationDetails; }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SalesInvoiceId { get; private set; }
    public string TaxCode { get; private set; } = "";
    public decimal NetAmount { get; private set; }
    public decimal VatAmount { get; private set; }
    public decimal GrossAmount { get; private set; }
    public decimal VatAmountPln { get; private set; }
}

public sealed class SalesInvoiceExchangeRateSnapshot
{
    private SalesInvoiceExchangeRateSnapshot() { }
    internal SalesInvoiceExchangeRateSnapshot(Guid tenantId, Guid invoiceId, ExchangeRateData data, string actor, DateTimeOffset now)
    { Id = data.Id; TenantId = tenantId; SalesInvoiceId = invoiceId; Data = data; CreatedBy = actor; CreatedAt = now; }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SalesInvoiceId { get; private set; }
    public ExchangeRateData Data { get; private set; } = new();
    public bool IsCurrent { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }
    public string CreatedBy { get; private set; } = "";
    internal void Supersede() => IsCurrent = false;
}

public sealed class SalesInvoiceDetails
{
    private SalesInvoiceDetails() { }
    internal SalesInvoiceDetails(Guid tenantId, Guid invoiceId) { TenantId = tenantId; SalesInvoiceId = invoiceId; }
    public Guid TenantId { get; private set; }
    public Guid SalesInvoiceId { get; private set; }
    public SalesInvoiceDetailsData Data { get; private set; } = new();
    internal void Replace(SalesInvoiceDetailsData data) => Data = data with { PartialPayments = Array.AsReadOnly(data.PartialPayments.ToArray()), Settlement = data.Settlement?.Freeze(), PaymentTerms = Array.AsReadOnly(data.PaymentTerms.ToArray()), BankAccounts = Array.AsReadOnly(data.BankAccounts.ToArray()), FactorBankAccounts = Array.AsReadOnly(data.FactorBankAccounts.ToArray()), AdditionalDescriptions = Array.AsReadOnly(data.AdditionalDescriptions.ToArray()) };
    public SalesInvoiceDetailsData Snapshot() => Data with { PartialPayments = Data.PartialPayments.ToArray(), Settlement = Data.Settlement?.Copy(), PaymentTerms = Data.PaymentTerms.ToArray(), BankAccounts = Data.BankAccounts.ToArray(), FactorBankAccounts = Data.FactorBankAccounts.ToArray(), AdditionalDescriptions = Data.AdditionalDescriptions.ToArray() };
}

// Reserved relationship model: no correction/advance issuing operations are exposed in stage 1.
public sealed class SalesInvoiceReference
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SalesInvoiceId { get; private set; }
    public InvoiceReferenceKind Kind { get; private set; }
    public Guid? ReferencedSalesInvoiceId { get; private set; }
    public Guid? ReferencedLineId { get; private set; }
    public int? ReferencedRevision { get; private set; }
    public string? ExternalNumber { get; private set; }
    public string? KsefNumber { get; private set; }
    public DateOnly? IssueDate { get; private set; }
}
