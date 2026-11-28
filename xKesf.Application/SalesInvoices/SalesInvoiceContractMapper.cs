using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Application.SalesInvoices;

public static partial class SalesInvoiceContractMapper
{
    public static SalesInvoiceDraft ToDomain(SalesInvoiceDraftDto dto)
    {
        if (dto is null || dto.Participants is null || dto.Participants.Any(x => x is null) || dto.Fa3 is null || dto.Fa3.Qualifications is null || dto.Fa3.WarehouseDocumentNumbers is null || dto.Lines is null || dto.ExchangeRates is null || dto.Details is null || dto.Details.PaymentTerms is null || dto.Details.BankAccounts is null || dto.Details.FactorBankAccounts is null || dto.Details.AdditionalDescriptions is null)
            throw Invalid();
        if (dto.Lines.Any(x => x is null) || dto.ExchangeRates.Any(x => x is null) || dto.Details.PaymentTerms.Any(x => x is null) || dto.Details.BankAccounts.Any(x => x is null) || dto.Details.FactorBankAccounts.Any(x => x is null) || dto.Details.AdditionalDescriptions.Any(x => x is null)) throw Invalid();
        return new()
        {
            ModelVersion = dto.ModelVersion, IssuePlace = dto.IssuePlace, SaleDateMode = (SaleDateMode?)dto.SaleDateMode, SalePeriod = dto.SalePeriod is null ? null : new(dto.SalePeriod.From, dto.SalePeriod.To), Participants = dto.Participants.Select(Participant).ToArray(), Fa3 = Fa3(dto.Fa3), NumberSeriesId = dto.NumberSeriesId, Kind = (SalesInvoiceKind)dto.Kind, PlannedIssueDate = dto.PlannedIssueDate, SaleDate = dto.SaleDate, Currency = dto.Currency,
            VatMethod = (VatCalculationMethod)dto.VatMethod, GlobalDiscount = Discount(dto.GlobalDiscount), GlobalDiscountBasis = (PriceBasis)dto.GlobalDiscountBasis,
            PdfLanguage = (PdfPresentationLanguage)dto.PdfLanguage, Notes = dto.Notes, Seller = Party(dto.Seller), Buyer = Party(dto.Buyer),
            Lines = dto.Lines.Select(x => new SalesInvoiceLineInput { Id = x.Id, Description = x.Description, Unit = x.Unit, Quantity = x.Quantity?.Value, UnitPrice = x.UnitPrice?.Value, PriceBasis = (PriceBasis)x.PriceBasis, TaxCode = x.TaxCode, Discount = Discount(x.Discount), ProductCode = x.ProductCode, Gtin = x.Gtin, Pkwiu = x.Pkwiu, Cn = x.Cn, Gtu = x.Gtu, SaleDate = x.SaleDate, TaxTreatment = TaxTreatment(x.TaxTreatment), ExemptionBasis = Basis(x.ExemptionBasis), Procedure = (InvoiceProcedureCode?)x.Procedure, Pkob = x.Pkob, Annex15 = Decision(x.Annex15), ExciseAmount = x.ExciseAmount?.Value }).ToArray(),
            ExchangeRates = dto.ExchangeRates.Select(x => new ExchangeRateData { Id = x.Id, FromCurrency = x.FromCurrency, ToCurrency = x.ToCurrency, Rate = x.Rate.Value, QuotedUnits = x.QuotedUnits.Value, OriginalRate = x.OriginalRate?.Value, Source = (ExchangeRateSource)x.Source, SourceReference = x.SourceReference, RateDate = x.RateDate, ReferenceDate = x.ReferenceDate, SelectionRule = x.SelectionRule, RetrievedAt = x.RetrievedAt, IsManualOverride = x.IsManualOverride, OverrideReason = x.OverrideReason, OriginalSnapshotId = x.OriginalSnapshotId }).ToArray(),
            Details = new() { Settlement = Settlement(dto.Details.Settlement), Version = dto.Details.Version, PaymentMethod = dto.Details.PaymentMethod, OtherPaymentDescription = dto.Details.OtherPaymentDescription,
                PaymentTerms = dto.Details.PaymentTerms.Select(x => new InvoicePaymentTerm(x.DueDate, x.Quantity, x.Unit, x.StartingEvent)).ToArray(),
                BankAccounts = dto.Details.BankAccounts.Select(Account).ToArray(),
                FactorBankAccounts = dto.Details.FactorBankAccounts.Select(Account).ToArray(),
                AdditionalDescriptions = dto.Details.AdditionalDescriptions.Select(x => new InvoiceAdditionalDescription(x.Key, x.Value, x.LineId)).ToArray() }
        };
    }
    public static SalesInvoiceDto ToDto(SalesInvoice invoice)
    {
        var draft = invoice.ToDraft();
        var missingData = SalesInvoiceCalculator.GetMissingData(draft);
        var approval = invoice.Approvals.OrderBy(x => x.ApprovedRevision).LastOrDefault();
        return new(invoice.Id, (int)invoice.Status, invoice.Revision, Convert.ToBase64String(invoice.RowVersion), Draft(draft), invoice.CalculationComplete, invoice.CalculationVersion,
            new(Exact(invoice.NetAmount), Exact(invoice.VatAmount), Exact(invoice.GrossAmount), Exact(invoice.VatAmountPln), invoice.GlobalDiscountAmount),
            invoice.Lines.OrderBy(x => x.Ordinal).Select(x => new SalesInvoiceLineResultDto(x.Id, Exact(x.NetAmount), Exact(x.VatAmount), Exact(x.GrossAmount), Exact(x.VatAmountPln), x.GlobalDiscountAllocation, x.VatRoundingAdjustment, (int?)x.VatAmountOrigin, Exact(x.VatBaseRoundingAdjustment))).ToArray(),
            invoice.TaxSummaries.OrderBy(x => x.TaxCode, StringComparer.Ordinal).Select(x => new SalesInvoiceTaxSummaryDto(x.TaxCode, x.NetAmount, x.VatAmount, x.GrossAmount, x.VatAmountPln, x.CalculationDetails is not { } details ? null : new(details.Version, (int)details.Method, (int)details.Basis, details.TaxBase, details.Rate))).ToArray(),
            invoice.ExchangeRates.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new ExchangeRateHistoryDto(Rate(x.Data), x.IsCurrent, x.CreatedAt, x.CreatedBy)).ToArray(),
            missingData.Select(x => new SalesInvoiceIssueDto(x.Path, x.Code, x.Message)).ToArray(), invoice.CreatedAt, invoice.UpdatedAt,
            approval is null ? null : new(approval.Id, approval.ApprovedBy, approval.ApprovedAt, approval.SourceRevision, approval.ApprovedRevision), invoice.InvoiceNumber, invoice.KsefNumber, invoice.FinalNumberReservationId, SalesInvoiceFa3Validator.Decisions(draft).Select(x => State(x.Path, x.Decision)).ToArray(),
            invoice.SettlementAmounts is not { } settlement ? null : new(Exact(settlement.ChargesTotal), Exact(settlement.DeductionsTotal), Exact(settlement.AmountDue), Exact(settlement.Overpayment)));
    }
    public static SalesInvoiceListDto ToDto(SalesInvoiceListPage page) => new(page.Items.Select(x => new SalesInvoiceListItemDto(x.Id, (int)x.Status, x.PlannedIssueDate, x.Currency, Exact(x.NetAmount), Exact(x.VatAmount), Exact(x.GrossAmount), x.UpdatedAt, x.Revision)).ToArray(), page.TotalCount, page.Page, page.PageSize);
    private static SalesInvoiceDraftDto Draft(SalesInvoiceDraft x) => new()
    {
        ModelVersion = x.ModelVersion, IssuePlace = x.IssuePlace, SaleDateMode = (int?)x.SaleDateMode, SalePeriod = x.SalePeriod is null ? null : new(x.SalePeriod.From, x.SalePeriod.To), Participants = x.Participants.Select(Participant).ToArray(), Fa3 = Fa3(x.Fa3), NumberSeriesId = x.NumberSeriesId, Kind = (int)x.Kind, PlannedIssueDate = x.PlannedIssueDate, SaleDate = x.SaleDate, Currency = x.Currency, VatMethod = (int)x.VatMethod, GlobalDiscount = Discount(x.GlobalDiscount), GlobalDiscountBasis = (int)x.GlobalDiscountBasis, PdfLanguage = (int)x.PdfLanguage, Notes = x.Notes,
        Seller = Party(x.Seller), Buyer = Party(x.Buyer),
        Lines = x.Lines.Select(l => new SalesInvoiceLineDto { Id = l.Id, Description = l.Description, Unit = l.Unit, Quantity = Exact(l.Quantity), UnitPrice = Exact(l.UnitPrice), PriceBasis = (int)l.PriceBasis, TaxCode = l.TaxCode, Discount = Discount(l.Discount), ProductCode = l.ProductCode, Gtin = l.Gtin, Pkwiu = l.Pkwiu, Cn = l.Cn, Gtu = l.Gtu, SaleDate = l.SaleDate, TaxTreatment = TaxTreatment(l.TaxTreatment), ExemptionBasis = Basis(l.ExemptionBasis), Procedure = (int?)l.Procedure, Pkob = l.Pkob, Annex15 = Decision(l.Annex15), ExciseAmount = Exact(l.ExciseAmount) }).ToArray(),
        ExchangeRates = x.ExchangeRates.Select(Rate).ToArray(), Details = new() { Settlement = x.Details.Settlement is not { } settlement ? null : new() { Charges = settlement.Charges.Select(e => new InvoiceSettlementEntryDto(Exact(e.Amount), e.Reason)).ToArray(), Deductions = settlement.Deductions.Select(e => new InvoiceSettlementEntryDto(Exact(e.Amount), e.Reason)).ToArray() }, Version = x.Details.Version, PaymentMethod = x.Details.PaymentMethod, OtherPaymentDescription = x.Details.OtherPaymentDescription,
            PaymentTerms = x.Details.PaymentTerms.Select(t => new PaymentTermDto(t.DueDate, t.Quantity, t.Unit, t.StartingEvent)).ToArray(), BankAccounts = x.Details.BankAccounts.Select(Account).ToArray(), FactorBankAccounts = x.Details.FactorBankAccounts.Select(Account).ToArray(), AdditionalDescriptions = x.Details.AdditionalDescriptions.Select(d => new AdditionalDescriptionDto(d.Key, d.Value, d.LineId)).ToArray() }
    };
    private static InvoiceBankAccount Account(BankAccountDto x) => new(x.Number, x.Swift, x.BankName, x.Description, (BankOwnAccountType?)x.BankOwnAccountType);
    private static InvoiceSettlementData? Settlement(InvoiceSettlementDto? x)
    {
        if (x is null) return null;
        if (x.Charges is null || x.Deductions is null || x.Charges.Any(e => e is null) || x.Deductions.Any(e => e is null)) throw Invalid();
        return new() { Charges = x.Charges.Select(e => new InvoiceSettlementEntry(e.Amount?.Value, e.Reason)).ToArray(), Deductions = x.Deductions.Select(e => new InvoiceSettlementEntry(e.Amount?.Value, e.Reason)).ToArray() };
    }
    private static BankAccountDto Account(InvoiceBankAccount x) => new(x.Number, x.Swift, x.BankName, x.Description, (int?)x.BankOwnAccountType);
    private static ExchangeRateDto Rate(ExchangeRateData x) => new() { Id = x.Id, FromCurrency = x.FromCurrency, ToCurrency = x.ToCurrency, Rate = x.Rate, QuotedUnits = x.QuotedUnits, OriginalRate = Exact(x.OriginalRate), Source = (int)x.Source, SourceReference = x.SourceReference, RateDate = x.RateDate, ReferenceDate = x.ReferenceDate, SelectionRule = x.SelectionRule, RetrievedAt = x.RetrievedAt, IsManualOverride = x.IsManualOverride, OverrideReason = x.OverrideReason, OriginalSnapshotId = x.OriginalSnapshotId };
    private static InvoiceDiscount? Discount(DiscountDto? x) => x is null ? null : new((DiscountKind)x.Kind, x.Value.Value);
    private static DiscountDto? Discount(InvoiceDiscount? x) => x is null ? null : new((int)x.Kind, x.Value);
    private static SalesInvoicePartyData? Party(InvoicePartyDto? x)
    {
        if (x is null) return null;
        if (x.Contacts is null || x.Contacts.Any(c => c is null)) throw Invalid();
        return new() { Name = x.Name, IdentifierKind = (TaxIdentifierKind)x.IdentifierKind, Identifier = x.Identifier, IdentifierCountryCode = x.IdentifierCountryCode, Address = Address(x.Address), CorrespondenceAddress = Address(x.CorrespondenceAddress), Contacts = x.Contacts.Select(c => new InvoiceContact(c.Email, c.Phone)).ToArray(), SourceContractorId = x.SourceContractorId, Eori = x.Eori, BuyerLinkKey = x.BuyerLinkKey, CustomerNumber = x.CustomerNumber, SellerStatus = (SellerInformationStatus?)x.SellerStatus, TaxpayerStatus = (QualificationValue)x.TaxpayerStatus, PlPrefix = Decision(x.PlPrefix) };
    }
    private static InvoicePartyDto? Party(SalesInvoicePartyData? x) => x is null ? null : new() { Name = x.Name, IdentifierKind = (int)x.IdentifierKind, Identifier = x.Identifier, IdentifierCountryCode = x.IdentifierCountryCode, Address = Address(x.Address), CorrespondenceAddress = Address(x.CorrespondenceAddress), Contacts = x.Contacts.Select(c => new InvoiceContactDto(c.Email, c.Phone)).ToArray(), SourceContractorId = x.SourceContractorId, Eori = x.Eori, BuyerLinkKey = x.BuyerLinkKey, CustomerNumber = x.CustomerNumber, SellerStatus = (int?)x.SellerStatus, TaxpayerStatus = (int)x.TaxpayerStatus, PlPrefix = Decision(x.PlPrefix) };
    private static InvoiceAddress? Address(InvoiceAddressDto? x) => x is null ? null : new(x.CountryCode, x.Line1, x.Line2, x.Gln);
    private static InvoiceAddressDto? Address(InvoiceAddress? x) => x is null ? null : new(x.CountryCode, x.Line1, x.Line2, x.Gln);
    private static ExactDecimal? Exact(decimal? value) => value is null ? null : new(value.Value);
    private static SalesInvoiceValidationException Invalid() => new([new("draft", "InvalidStructure", "Nieprawidłowa struktura danych projektu.")]);
}
