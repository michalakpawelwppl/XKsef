using xKsef.Application.Contractors;
using xKsef.Application.Tenants;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;
using xKsef.Application.SalesInvoices.Snapshots;
using xKsef.Application.InvoiceNumbering;

namespace xKsef.Application.SalesInvoices;

public sealed class SalesInvoiceService(ISalesInvoiceRepository repository, ITenantService tenants, IContractorService contractors, SalesInvoiceCalculator calculator, TimeProvider clock, SalesInvoiceIssuanceValidator issuanceValidator, ITenantPermissionService permissions, IInvoiceNumberSeriesRepository numberSeries, ISalesInvoiceApprovalSnapshotWriter snapshots) : ISalesInvoiceService
{
    public async Task<SalesInvoice> CreateAsync(Guid tenantId, string userId, SalesInvoiceDraft draft, CancellationToken cancellationToken = default)
    {
        var prepared = await PrepareAsync(tenantId, draft, null, cancellationToken);
        var invoice = SalesInvoice.Create(tenantId, prepared, userId, clock.GetUtcNow(), calculator);
        await repository.AddAsync(invoice, cancellationToken);
        return invoice;
    }

    public Task<SalesInvoice?> GetAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default) => repository.GetAsync(tenantId, id, false, cancellationToken);

    public async Task<SalesInvoiceIssuanceValidationResult> ValidateIssuanceAsync(Guid tenantId, Guid id, CancellationToken cancellationToken = default)
    {
        var invoice = await repository.GetAsync(tenantId, id, false, cancellationToken) ?? throw new KeyNotFoundException();
        return await ValidateForIssuanceAsync(invoice, cancellationToken);
    }

    public async Task<SalesInvoice> ApproveAsync(Guid tenantId, Guid id, string userId, byte[] expectedVersion, int expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync(userId, tenantId, TenantPermissions.SalesInvoicesIssue, cancellationToken))
            throw new TenantPermissionDeniedException(TenantPermissions.SalesInvoicesIssue);
        var invoice = await LoadAsync(tenantId, id, expectedVersion, cancellationToken);
        if (invoice.Revision != expectedRevision) throw new SalesInvoiceConflictException();
        var validation = await ValidateForIssuanceAsync(invoice, cancellationToken);
        if (!validation.IsReadyForIssuance) throw new SalesInvoiceValidationException(validation.Errors);
        // No await or reload between validation, snapshot capture and transition.
        var approvalId = Guid.NewGuid(); var approvedAt = clock.GetUtcNow();
        var snapshot = snapshots.Write(invoice, approvalId, userId, approvedAt, validation.Calculations);
        invoice.Approve(expectedRevision, userId, approvedAt, snapshot, approvalId);
        await repository.SaveAsync(invoice, expectedVersion, cancellationToken);
        return invoice;
    }

    public async Task<SalesInvoice> UnapproveAsync(Guid tenantId, Guid id, string userId, byte[] expectedVersion, int expectedRevision, CancellationToken cancellationToken = default)
    {
        if (!await permissions.HasPermissionAsync(userId, tenantId, TenantPermissions.SalesInvoicesUnapprove, cancellationToken))
            throw new TenantPermissionDeniedException(TenantPermissions.SalesInvoicesUnapprove);
        return await repository.UnapproveAsync(tenantId, id, expectedVersion, expectedRevision, userId, clock.GetUtcNow(), cancellationToken);
    }

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

    private async Task<SalesInvoiceIssuanceValidationResult> ValidateForIssuanceAsync(SalesInvoice invoice, CancellationToken cancellationToken)
    {
        var selected = invoice.NumberSeriesId is { } id ? await numberSeries.GetAsync(invoice.TenantId, id, false, cancellationToken) : null;
        var result = issuanceValidator.Validate(invoice);
        return selected is { IsActive: true } ? result : new(result.Errors.Concat([new InvoiceValidationIssue("numberSeriesId", "ActiveSeriesRequired", "Przed zatwierdzeniem wybierz aktywną serię numeracyjną tego tenanta.")]).ToArray(), result.Warnings);
    }

    private async Task<SalesInvoiceDraft> PrepareAsync(Guid tenantId, SalesInvoiceDraft draft, SalesInvoice? existing, CancellationToken cancellationToken)
    {
        if (tenantId == Guid.Empty) throw Invalid("tenantId", "Nieprawidłowy tenant.");
        if (draft is null || draft.Lines is null || draft.ExchangeRates is null || draft.Participants is null || draft.Fa3 is null) throw Invalid("draft", "Nieprawidłowa struktura projektu.");
        var taxEntity = await tenants.GetTaxEntityAsync(tenantId, cancellationToken) ?? throw new KeyNotFoundException();
        var selectedSeriesId = draft.NumberSeriesId ?? existing?.NumberSeriesId;
        if (selectedSeriesId is null && existing is null) selectedSeriesId = (await numberSeries.GetDefaultAsync(tenantId, draft.Kind, cancellationToken))?.SeriesId;
        if (selectedSeriesId is { } selectedId)
        {
            var series = await numberSeries.GetAsync(tenantId, selectedId, false, cancellationToken);
            if (series is null || !series.IsActive) throw Invalid("numberSeriesId", "Wybierz aktywną serię numeracyjną tego tenanta.");
        }
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
        async Task<SalesInvoicePartyData?> PrepareParty(SalesInvoicePartyData? party, SalesInvoicePartyData? old, string path)
        {
            if (party?.SourceContractorId is { } contractorId)
            {
                var contractor = await contractors.GetByIdAsync(tenantId, contractorId, cancellationToken) ?? throw Invalid(path + ".sourceContractorId", "Kontrahent nie istnieje w tenantcie.");
                if (old?.SourceContractorId != contractorId)
                {
                    var data = contractor.ContractorData;
                    var address = contractor.Addresses.FirstOrDefault()?.AddressData;
                    var eu = data.EuVatNumber;
                    party = party with
                    {
                        Name = data.FullName,
                        IdentifierKind = data.Nip is not null ? TaxIdentifierKind.Nip : eu is not null ? TaxIdentifierKind.EuVat : data.OtherTaxNumber is not null ? TaxIdentifierKind.Other : TaxIdentifierKind.None,
                        Identifier = data.Nip is not null ? NormalizeNip(data.Nip) : eu is not null && eu.Length >= 3 ? eu[2..] : data.OtherTaxNumber,
                        IdentifierCountryCode = eu is not null && eu.Length >= 3 ? eu[..2] : null,
                        Address = address is null ? null : new InvoiceAddress(address.CountryCode, string.Join(" ", new[] { address.Street, address.BuildingNumber, address.ApartmentNumber is null ? null : "/" + address.ApartmentNumber }.Where(x => !string.IsNullOrWhiteSpace(x))), address.PostalCode + " " + address.City)
                    };
                }
            }
            return ValidateIdentifier(party, path);
        }
        var oldDraft = existing?.ToDraft();
        var buyer = await PrepareParty(draft.Buyer, oldDraft?.Buyer, "buyer");
        var participants = new List<InvoiceParticipantData>();
        foreach (var participant in draft.Participants)
        {
            if (participant is null) throw Invalid("participants", "Uczestnik nie może być null.");
            var old = oldDraft?.Participants.SingleOrDefault(x => x.Id == participant.Id);
            if (participant.Id != Guid.Empty && old is null) throw Invalid("participants.id", "Nowy uczestnik nie może wskazywać istniejącego identyfikatora.");
            participants.Add(participant with { Id = participant.Id == Guid.Empty ? Guid.NewGuid() : participant.Id,
                Party = await PrepareParty(participant.Party, old?.Party, "participants.party") ?? throw Invalid("participants.party", "Brak danych uczestnika.") });
        }
        var fa3 = draft.Fa3;
        if (fa3.AuthorizedParty is { } authorized)
            fa3 = fa3 with { AuthorizedParty = authorized with { Party = await PrepareParty(authorized.Party, oldDraft?.Fa3.AuthorizedParty?.Party, "fa3.authorizedParty.party") ?? throw Invalid("fa3.authorizedParty.party", "Brak danych podmiotu.") } };
        var rates = draft.ExchangeRates.Select(x => x is null ? throw Invalid("exchangeRates", "Kurs nie może być null.") : x with { Id = x.Id == Guid.Empty ? Guid.NewGuid() : x.Id }).ToArray();
        return draft with { NumberSeriesId = selectedSeriesId, Seller = seller, Buyer = buyer, Lines = lines, ExchangeRates = rates, Participants = participants, Fa3 = fa3 };
    }

    internal static SalesInvoicePartyData? ValidateIdentifier(SalesInvoicePartyData? party, string path)
    {
        if (party is null || string.IsNullOrWhiteSpace(party.Identifier)) return party;
        try
        {
            if (party.IdentifierKind == TaxIdentifierKind.Nip)
            {
                ContractorIdentifierValidator.NormalizeAndValidateNip(party.Identifier);
                return party with { Identifier = NormalizeNip(party.Identifier) };
            }
            if (party.IdentifierKind == TaxIdentifierKind.InternalId && !System.Text.RegularExpressions.Regex.IsMatch(party.Identifier, @"^[1-9]((\d[1-9])|([1-9]\d))\d{7}-\d{5}$")) throw new ArgumentException();
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
