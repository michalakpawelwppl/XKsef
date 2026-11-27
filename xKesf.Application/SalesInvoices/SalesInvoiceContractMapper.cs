using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Application.SalesInvoices;

public static class SalesInvoiceContractMapper
{
    public static SalesInvoiceDraft ToDomain(SalesInvoiceDraftDto dto)
    {
        if (dto is null || dto.Lines is null || dto.ExchangeRates is null || dto.Details is null || dto.Details.PaymentTerms is null || dto.Details.BankAccounts is null || dto.Details.AdditionalDescriptions is null)
            throw Invalid();
        if (dto.Lines.Any(x => x is null) || dto.ExchangeRates.Any(x => x is null) || dto.Details.PaymentTerms.Any(x => x is null) || dto.Details.BankAccounts.Any(x => x is null) || dto.Details.AdditionalDescriptions.Any(x => x is null)) throw Invalid();
        return new()
        {
            Kind = (SalesInvoiceKind)dto.Kind, PlannedIssueDate = dto.PlannedIssueDate, SaleDate = dto.SaleDate, Currency = dto.Currency,
            VatMethod = (VatCalculationMethod)dto.VatMethod, GlobalDiscount = Discount(dto.GlobalDiscount), GlobalDiscountBasis = (PriceBasis)dto.GlobalDiscountBasis,
            PdfLanguage = (PdfPresentationLanguage)dto.PdfLanguage, Notes = dto.Notes, Seller = Party(dto.Seller), Buyer = Party(dto.Buyer),
            Lines = dto.Lines.Select(x => new SalesInvoiceLineInput { Id = x.Id, Description = x.Description, Unit = x.Unit, Quantity = x.Quantity?.Value, UnitPrice = x.UnitPrice?.Value, PriceBasis = (PriceBasis)x.PriceBasis, TaxCode = x.TaxCode, Discount = Discount(x.Discount), ProductCode = x.ProductCode, Gtin = x.Gtin, Pkwiu = x.Pkwiu, Cn = x.Cn, Gtu = x.Gtu }).ToArray(),
            ExchangeRates = dto.ExchangeRates.Select(x => new ExchangeRateData { Id = x.Id, FromCurrency = x.FromCurrency, ToCurrency = x.ToCurrency, Rate = x.Rate.Value, QuotedUnits = x.QuotedUnits.Value, OriginalRate = x.OriginalRate?.Value, Source = (ExchangeRateSource)x.Source, SourceReference = x.SourceReference, RateDate = x.RateDate, ReferenceDate = x.ReferenceDate, SelectionRule = x.SelectionRule, RetrievedAt = x.RetrievedAt, IsManualOverride = x.IsManualOverride, OverrideReason = x.OverrideReason, OriginalSnapshotId = x.OriginalSnapshotId }).ToArray(),
            Details = new() { Version = dto.Details.Version, PaymentMethod = dto.Details.PaymentMethod, OtherPaymentDescription = dto.Details.OtherPaymentDescription,
                PaymentTerms = dto.Details.PaymentTerms.Select(x => new InvoicePaymentTerm(x.DueDate, x.Quantity, x.Unit, x.StartingEvent)).ToArray(),
                BankAccounts = dto.Details.BankAccounts.Select(x => new InvoiceBankAccount(x.Number, x.Swift, x.BankName, x.Description)).ToArray(),
                AdditionalDescriptions = dto.Details.AdditionalDescriptions.Select(x => new InvoiceAdditionalDescription(x.Key, x.Value, x.LineId)).ToArray() }
        };
    }
    public static SalesInvoiceDto ToDto(SalesInvoice invoice)
    {
        var draft = invoice.ToDraft();
        var missingData = SalesInvoiceCalculator.GetMissingData(draft);
        return new(invoice.Id, (int)invoice.Status, invoice.Revision, Convert.ToBase64String(invoice.RowVersion), Draft(draft), invoice.CalculationComplete, invoice.CalculationVersion,
            new(Exact(invoice.NetAmount), Exact(invoice.VatAmount), Exact(invoice.GrossAmount), Exact(invoice.VatAmountPln), invoice.GlobalDiscountAmount),
            invoice.Lines.OrderBy(x => x.Ordinal).Select(x => new SalesInvoiceLineResultDto(x.Id, Exact(x.NetAmount), Exact(x.VatAmount), Exact(x.GrossAmount), Exact(x.VatAmountPln), x.GlobalDiscountAllocation, x.VatRoundingAdjustment)).ToArray(),
            invoice.TaxSummaries.OrderBy(x => x.TaxCode, StringComparer.Ordinal).Select(x => new SalesInvoiceTaxSummaryDto(x.TaxCode, x.NetAmount, x.VatAmount, x.GrossAmount, x.VatAmountPln)).ToArray(),
            invoice.ExchangeRates.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new ExchangeRateHistoryDto(Rate(x.Data), x.IsCurrent, x.CreatedAt, x.CreatedBy)).ToArray(),
            missingData.Select(x => new SalesInvoiceIssueDto(x.Path, x.Code, x.Message)).ToArray(), invoice.CreatedAt, invoice.UpdatedAt);
    }
    public static SalesInvoiceListDto ToDto(SalesInvoiceListPage page) => new(page.Items.Select(x => new SalesInvoiceListItemDto(x.Id, (int)x.Status, x.PlannedIssueDate, x.Currency, Exact(x.NetAmount), Exact(x.VatAmount), Exact(x.GrossAmount), x.UpdatedAt, x.Revision)).ToArray(), page.TotalCount, page.Page, page.PageSize);
    private static SalesInvoiceDraftDto Draft(SalesInvoiceDraft x) => new()
    {
        Kind = (int)x.Kind, PlannedIssueDate = x.PlannedIssueDate, SaleDate = x.SaleDate, Currency = x.Currency, VatMethod = (int)x.VatMethod, GlobalDiscount = Discount(x.GlobalDiscount), GlobalDiscountBasis = (int)x.GlobalDiscountBasis, PdfLanguage = (int)x.PdfLanguage, Notes = x.Notes,
        Seller = Party(x.Seller), Buyer = Party(x.Buyer),
        Lines = x.Lines.Select(l => new SalesInvoiceLineDto { Id = l.Id, Description = l.Description, Unit = l.Unit, Quantity = Exact(l.Quantity), UnitPrice = Exact(l.UnitPrice), PriceBasis = (int)l.PriceBasis, TaxCode = l.TaxCode, Discount = Discount(l.Discount), ProductCode = l.ProductCode, Gtin = l.Gtin, Pkwiu = l.Pkwiu, Cn = l.Cn, Gtu = l.Gtu }).ToArray(),
        ExchangeRates = x.ExchangeRates.Select(Rate).ToArray(), Details = new() { Version = x.Details.Version, PaymentMethod = x.Details.PaymentMethod, OtherPaymentDescription = x.Details.OtherPaymentDescription,
            PaymentTerms = x.Details.PaymentTerms.Select(t => new PaymentTermDto(t.DueDate, t.Quantity, t.Unit, t.StartingEvent)).ToArray(), BankAccounts = x.Details.BankAccounts.Select(b => new BankAccountDto(b.Number, b.Swift, b.BankName, b.Description)).ToArray(), AdditionalDescriptions = x.Details.AdditionalDescriptions.Select(d => new AdditionalDescriptionDto(d.Key, d.Value, d.LineId)).ToArray() }
    };
    private static ExchangeRateDto Rate(ExchangeRateData x) => new() { Id = x.Id, FromCurrency = x.FromCurrency, ToCurrency = x.ToCurrency, Rate = x.Rate, QuotedUnits = x.QuotedUnits, OriginalRate = Exact(x.OriginalRate), Source = (int)x.Source, SourceReference = x.SourceReference, RateDate = x.RateDate, ReferenceDate = x.ReferenceDate, SelectionRule = x.SelectionRule, RetrievedAt = x.RetrievedAt, IsManualOverride = x.IsManualOverride, OverrideReason = x.OverrideReason, OriginalSnapshotId = x.OriginalSnapshotId };
    private static InvoiceDiscount? Discount(DiscountDto? x) => x is null ? null : new((DiscountKind)x.Kind, x.Value.Value);
    private static DiscountDto? Discount(InvoiceDiscount? x) => x is null ? null : new((int)x.Kind, x.Value);
    private static SalesInvoicePartyData? Party(InvoicePartyDto? x)
    {
        if (x is null) return null;
        if (x.Contacts is null || x.Contacts.Any(c => c is null)) throw Invalid();
        return new() { Name = x.Name, IdentifierKind = (TaxIdentifierKind)x.IdentifierKind, Identifier = x.Identifier, IdentifierCountryCode = x.IdentifierCountryCode, Address = Address(x.Address), CorrespondenceAddress = Address(x.CorrespondenceAddress), Contacts = x.Contacts.Select(c => new InvoiceContact(c.Email, c.Phone)).ToArray(), SourceContractorId = x.SourceContractorId, Eori = x.Eori };
    }
    private static InvoicePartyDto? Party(SalesInvoicePartyData? x) => x is null ? null : new() { Name = x.Name, IdentifierKind = (int)x.IdentifierKind, Identifier = x.Identifier, IdentifierCountryCode = x.IdentifierCountryCode, Address = Address(x.Address), CorrespondenceAddress = Address(x.CorrespondenceAddress), Contacts = x.Contacts.Select(c => new InvoiceContactDto(c.Email, c.Phone)).ToArray(), SourceContractorId = x.SourceContractorId, Eori = x.Eori };
    private static InvoiceAddress? Address(InvoiceAddressDto? x) => x is null ? null : new(x.CountryCode, x.Line1, x.Line2, x.Gln);
    private static InvoiceAddressDto? Address(InvoiceAddress? x) => x is null ? null : new(x.CountryCode, x.Line1, x.Line2, x.Gln);
    private static ExactDecimal? Exact(decimal? value) => value is null ? null : new(value.Value);
    private static SalesInvoiceValidationException Invalid() => new([new("draft", "InvalidStructure", "Nieprawidłowa struktura danych projektu.")]);
}
