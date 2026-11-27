using xKsef.Application.Contractors;
using xKsef.Application.Tenants;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Application.SalesInvoices;

public sealed class SalesInvoiceService(ISalesInvoiceRepository repository, ITenantService tenants, IContractorService contractors, SalesInvoiceCalculator calculator, TimeProvider clock) : ISalesInvoiceService
{
    public async Task<SalesInvoice> CreateAsync(Guid tenantId, string userId, SalesInvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(tenantId, draft, null, cancellationToken);
        var invoice = SalesInvoice.Create(tenantId, prepared, userId, clock.GetUtcNow(), calculator);
        await repository.AddAsync(invoice, cancellationToken);
        return invoice;
    }

    public Task<SalesInvoice?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) => repository.GetAsync(tenantId, id, false, cancellationToken);

    public async Task<SalesInvoice> UpdateAsync(Guid tenantId, Guid id, string userId, byte[] expectedVersion, SalesInvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(tenantId, id, expectedVersion, cancellationToken);
        var prepared = await PrepareAsync(tenantId, draft, invoice, cancellationToken);
        invoice.UpdateDraft(prepared, userId, clock.GetUtcNow(), calculator);
        await repository.SaveAsync(invoice, expectedVersion, cancellationToken);
        return invoice;
    }

    public async Task<SalesInvoice> RecalculateAsync(Guid tenantId, Guid id, string userId, byte[] expectedVersion, CancellationToken cancellationToken = default)
    {
        var invoice = await LoadAsync(tenantId, id, expectedVersion, cancellationToken);
        invoice.UpdateDraft(invoice.ToDraft(), userId, clock.GetUtcNow(), calculator);
        await repository.SaveAsync(invoice, expectedVersion, cancellationToken);
        return invoice;
    }

    public Task<SalesInvoiceListPage> ListDraftsAsync(Guid tenantId, int page, int pageSize, CancellationToken cancellationToken = default)
    {
        if (page < 1 || page > 1000000 || pageSize is < 1 or > 100) throw Invalid("pagination", "Strona musi być dodatnia, rozmiar 1–100.");
        return repository.ListDraftsAsync(tenantId, page, pageSize, cancellationToken);
    }

    private async Task<SalesInvoice> LoadAsync(Guid tenantId, Guid id, byte[] version, CancellationToken cancellationToken)
    {
        var invoice = await repository.GetAsync(tenantId, id, true, cancellationToken) ?? throw new KeyNotFoundException();
        if (version is null || version.Length != 8) throw Invalid("version", "Podaj wersję dokumentu (8 bajtów, Base64).");
        if (!invoice.RowVersion.SequenceEqual(version) || invoice.Status != SalesInvoiceStatus.Draft) throw new SalesInvoiceConflictException();
        return invoice;
    }

    private async Task<SalesInvoiceDraft> PrepareAsync(Guid tenantId, SalesInvoiceDraft draft, SalesInvoice? existing, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw Invalid("tenantId", "Nieprawidłowy tenant.");
        if (draft is null || draft.Lines is null || draft.ExchangeRates is null) throw Invalid("draft", "Nieprawidłowa struktura projektu.");
        var taxEntity = await tenants.GetTaxEntityAsync(tenantId, cancellationToken) ?? throw new KeyNotFoundException();
        var lines = new List<SalesInvoiceLineInput>();
        var oldIds = existing?.Lines.Select(x => x.Id).ToHashSet() ?? [];
        foreach (var line in draft.Lines)
        {
            if (line is null) throw Invalid("lines", "Pozycja nie może być null.");
            if (line.Id != Guid.Empty && !oldIds.Contains(line.Id)) throw Invalid("lines.id", "Nowa pozycja nie może wskazywać istniejącego identyfikatora.");
            lines.Add(line with { Id = line.Id == Guid.Empty ? Guid.NewGuid() : line.Id });
        }
        var oldSeller = existing?.ToDraft().Seller;
        var seller = draft.Seller ?? oldSeller ?? new SalesInvoicePartyData { Name = taxEntity.Name, Identifier = taxEntity.Nip };
        var expectedNip = oldSeller?.Identifier ?? taxEntity.Nip;
        if (seller.SourceContractorId is not null || seller.IdentifierKind != TaxIdentifierKind.Nip || seller.Identifier is not null && NormalizeNip(seller.Identifier) != expectedNip)
            throw Invalid("seller.identifier", "Sprzedawca musi należeć do podmiotu podatkowego tenanta.");
        seller = seller with { IdentifierKind = TaxIdentifierKind.Nip, Identifier = expectedNip };
        var buyer = draft.Buyer;
        if (buyer?.SourceContractorId is { } contractorId)
        {
            var contractor = await contractors.GetByIdAsync(tenantId, contractorId, cancellationToken) ?? throw Invalid("buyer.sourceContractorId", "Kontrahent nie istnieje w tenantcie.");
            // A source is copied only when first selected; ordinary reads/updates never refresh the snapshot.
            if (existing?.ToDraft().Buyer?.SourceContractorId != contractorId)
            {
                var data = contractor.ContractorData;
                var address = contractor.Addresses.FirstOrDefault()?.AddressData;
                var identifierKind = data.Nip is not null ? TaxIdentifierKind.Nip : data.EuVatNumber is not null ? TaxIdentifierKind.EuVat : data.OtherTaxNumber is not null ? TaxIdentifierKind.Other : TaxIdentifierKind.None;
                var eu = data.EuVatNumber;
                buyer = buyer with
                {
                    Name = data.FullName, IdentifierKind = identifierKind,
                    Identifier = data.Nip is not null ? NormalizeNip(data.Nip) : eu is not null && eu.Length >= 3 ? eu[2..] : data.OtherTaxNumber,
                    IdentifierCountryCode = eu is not null && eu.Length >= 3 ? eu[..2] : null,
                    Address = address is null ? null : new InvoiceAddress(address.CountryCode, string.Join(" ", new[] { address.Street, address.BuildingNumber, address.ApartmentNumber is null ? null : "/" + address.ApartmentNumber }.Where(x => !string.IsNullOrWhiteSpace(x))), address.PostalCode + " " + address.City)
                };
            }
        }
        buyer = ValidateIdentifier(buyer, "buyer");
        var rates = draft.ExchangeRates.Select(x => x is null ? throw Invalid("exchangeRates", "Kurs nie może być null.") : x with { Id = x.Id == Guid.Empty ? Guid.NewGuid() : x.Id }).ToArray();
        return draft with { Seller = seller, Buyer = buyer, Lines = lines, ExchangeRates = rates };
    }

    private static SalesInvoicePartyData? ValidateIdentifier(SalesInvoicePartyData? party, string path)
    {
        if (party is null || string.IsNullOrWhiteSpace(party.Identifier)) return party;
        try
        {
            if (party.IdentifierKind == TaxIdentifierKind.Nip)
            {
                ContractorIdentifierValidator.NormalizeAndValidateNip(party.Identifier);
                return party with { Identifier = NormalizeNip(party.Identifier) };
            }
            if (party.IdentifierKind == TaxIdentifierKind.EuVat && (party.Identifier.Length > 12 || party.IdentifierCountryCode is null || !System.Text.RegularExpressions.Regex.IsMatch(party.IdentifierCountryCode, "^(AT|BE|BG|CY|CZ|DE|DK|EE|EL|ES|FI|FR|HR|HU|IE|IT|LT|LU|LV|MT|NL|PL|PT|RO|SE|SI|SK|XI)$") || !System.Text.RegularExpressions.Regex.IsMatch(party.Identifier, "^[A-Z0-9+*]{1,12}$")))
                throw new ArgumentException();
            if (party.IdentifierKind == TaxIdentifierKind.None) throw new ArgumentException();
        }
        catch (ArgumentException) { throw Invalid(path + ".identifier", "Nieprawidłowy identyfikator podatkowy."); }
        return party;
    }
    private static string NormalizeNip(string value) => value.Replace(" ", "", StringComparison.Ordinal).Replace("-", "", StringComparison.Ordinal);
    private static SalesInvoiceValidationException Invalid(string path, string message) => new([new(path, "Invalid", message)]);
}
