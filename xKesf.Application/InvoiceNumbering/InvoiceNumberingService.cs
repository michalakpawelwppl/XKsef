using xKsef.Application.SalesInvoices.Snapshots;
using xKsef.Application.Tenants;
using xKsef.Domain.InvoiceNumberSeries;
using xKsef.Domain.SalesInvoices;
using NumberSeries = xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries;

namespace xKsef.Application.InvoiceNumbering;

public sealed record ReserveInvoiceNumberCommand(Guid InvoiceId, Guid ApprovalId, Guid OperationId, string InvoiceVersion);
public sealed record InvoiceNumberReservationResult(Guid Id, Guid InvoiceId, Guid ApprovalId, Guid OperationId, Guid SeriesId, int Period, long NumericValue, string FullNumber, DateOnly IssueDate, InvoiceNumberReservationState State, string Decision, string Version);

public interface IInvoiceIssuanceOutcomeSource
{
    // Local, durable evidence from the future issuing process. Implementations must not infer failure from timeouts.
    Task<VerifiedInvoiceIssuanceOutcome?> GetVerifiedOutcomeAsync(Guid tenantId, Guid operationId, CancellationToken cancellationToken);
}
public sealed class UnavailableInvoiceIssuanceOutcomeSource : IInvoiceIssuanceOutcomeSource
{
    public Task<VerifiedInvoiceIssuanceOutcome?> GetVerifiedOutcomeAsync(Guid tenantId, Guid operationId, CancellationToken cancellationToken) => Task.FromResult<VerifiedInvoiceIssuanceOutcome?>(null);
}
public interface IInvoiceNumberingRepository
{
    Task<T> ExecuteAsync<T>(Guid tenant, Func<CancellationToken, Task<T>> action, CancellationToken cancellationToken);
    Task<SalesInvoice?> GetInvoiceAsync(Guid tenant, Guid invoice, CancellationToken cancellationToken);
    Task<NumberSeries?> GetSeriesAsync(Guid tenant, Guid series, CancellationToken cancellationToken);
    Task<InvoiceNumberReservation?> GetOperationAsync(Guid tenant, Guid operation, CancellationToken cancellationToken);
    Task<IReadOnlyList<InvoiceNumberReservation>> GetReservationsAsync(Guid tenant, CancellationToken cancellationToken);
    Task<IReadOnlyList<string>> GetImportedSalesNumbersAsync(Guid tenant, CancellationToken cancellationToken);
    Task<InvoiceNumberCounter?> GetCounterAsync(Guid tenant, Guid series, int period, CancellationToken cancellationToken);
    void AddCounter(InvoiceNumberCounter counter);
    void AddReservation(InvoiceNumberReservation reservation);
}
public interface IInvoiceNumberingService
{
    Task<InvoiceNumberReservationResult> ReserveAsync(Guid tenant, string actor, ReserveInvoiceNumberCommand command, CancellationToken cancellationToken = default);
    Task<InvoiceNumberReservationResult> MarkUncertainAsync(Guid tenant, Guid invoice, Guid operation, string actor, string reason, CancellationToken cancellationToken = default);
    Task<InvoiceNumberReservationResult> ReleaseAsync(Guid tenant, Guid invoice, Guid operation, string actor, CancellationToken cancellationToken = default);
    Task<InvoiceNumberReservationResult> FinalizeAsync(Guid tenant, Guid invoice, Guid operation, string actor, CancellationToken cancellationToken = default);
}

/// <summary>Internal commands for the future online issuing process. No controller exposes these operations.</summary>
public sealed class InvoiceNumberingService(IInvoiceNumberingRepository repository, ITenantPermissionService permissions, IInvoiceIssuanceOutcomeSource outcomes, TimeProvider clock, ISalesInvoiceApprovalSnapshotReader snapshots) : IInvoiceNumberingService
{
    public async Task<InvoiceNumberReservationResult> ReserveAsync(Guid tenant, string actor, ReserveInvoiceNumberCommand command, CancellationToken cancellationToken = default)
        => Result(await ExecuteAsync(tenant, actor, async ct =>
        {
            if (command.OperationId == Guid.Empty || command.ApprovalId == Guid.Empty || command.InvoiceId == Guid.Empty) throw Conflict("InvalidCommand", "Podaj identyfikator operacji, faktury i zatwierdzenia.");
            var existing = await repository.GetOperationAsync(tenant, command.OperationId, ct);
            if (existing is not null)
            {
                if (existing.InvoiceId != command.InvoiceId || existing.ApprovalId != command.ApprovalId || existing.InvoiceVersion != command.InvoiceVersion) throw Conflict("OperationKeyReused", "Identyfikator operacji został już użyty z innymi danymi.");
                return existing;
            }
            var invoice = await repository.GetInvoiceAsync(tenant, command.InvoiceId, ct) ?? throw new KeyNotFoundException();
            if (invoice.Status != SalesInvoiceStatus.Approved || invoice.InvoiceNumber is not null || !invoice.RowVersion.SequenceEqual(Version(command.InvoiceVersion))) throw Conflict("InvoiceVersionConflict", "Faktura nie jest aktualnym zatwierdzonym dokumentem bez numeru ostatecznego.");
            var approval = invoice.Approvals.OrderBy(x => x.ApprovedRevision).LastOrDefault();
            if (approval?.Id != command.ApprovalId || approval.ApprovedRevision != invoice.Revision) throw Conflict("ApprovalConflict", "Rezerwacja wymaga bieżącej zatwierdzonej rewizji.");
            var snapshot = snapshots.Read(approval);
            if (invoice.NumberSeriesId is not { } seriesId || snapshot.Input?.NumberSeriesId != seriesId || snapshot.Input?.PlannedIssueDate != invoice.PlannedIssueDate || invoice.PlannedIssueDate is not { } issueDate)
                throw Conflict("ApprovalConflict", "Seria i data muszą pochodzić z zatwierdzonego dokumentu.");
            if (!ApprovedSalesInvoiceComparison.Matches(invoice, snapshot))
                throw Conflict("ApprovalConflict", "Dane faktury nie odpowiadają zatwierdzonemu snapshotowi.");
            var series = await repository.GetSeriesAsync(tenant, seriesId, ct) ?? throw new KeyNotFoundException();
            if (!series.IsActive) throw Conflict("SeriesInactive", "Wybrana seria jest nieaktywna.");
            var reservations = await repository.GetReservationsAsync(tenant, ct);
            if (reservations.Any(x => x.InvoiceId == invoice.Id && x.HoldsNumber)) throw Conflict("InvoiceAlreadyReserved", "Faktura posiada już aktywny, zablokowany lub wykorzystany numer.");
            var period = InvoiceNumberCounter.PeriodFor(series.Configuration.ResetMode, issueDate);
            var counter = await repository.GetCounterAsync(tenant, seriesId, period, ct);
            if (counter is null) { counter = new(tenant, seriesId, period, series.Configuration.StartingNumber, actor, clock.GetUtcNow()); repository.AddCounter(counter); }
            var imported = await repository.GetImportedSalesNumbersAsync(tenant, ct);
            var held = reservations.Where(x => x.HoldsNumber).ToArray();
            var occupied = held.Select(x => x.NormalizedNumber).Concat(imported.Select(x => x.ToUpperInvariant())).ToHashSet(StringComparer.Ordinal);
            var inPeriod = held.Where(x => x.SeriesId == seriesId && x.Period == period).ToArray();
            long? chosen = null; var decision = "NewCounterValue: brak zwolnionych numerów.";
            foreach (var candidate in reservations.Where(x => x.SeriesId == seriesId && x.Period == period && x.State == InvoiceNumberReservationState.Released).Select(x => x.NumericValue).Distinct().Order())
            {
                if (inPeriod.Any(x => x.NumericValue == candidate)) continue;
                if (imported.Count != 0) { decision = "NewCounterValue: nie można ustalić chronologii numerów zaimportowanych dokumentów."; continue; }
                if (inPeriod.Any(x => x.NumericValue < candidate && x.IssueDate > issueDate || x.NumericValue > candidate && x.IssueDate < issueDate))
                { decision = "NewCounterValue: zwolniony numer koliduje z chronologią dokumentów lub nierozstrzygniętych rezerwacji."; continue; }
                if (occupied.Contains(InvoiceNumberFormat.Render(series.Configuration, candidate, issueDate).ToUpperInvariant()))
                { decision = "NewCounterValue: pełny numer po zmianie formatu jest już zajęty."; continue; }
                chosen = candidate; decision = "ReleasedNumberReused: najniższy bezpieczny zwolniony numer."; break;
            }
            if (chosen is null)
            {
                long skipped = 0;
                while (true)
                {
                    chosen = counter.TakeNext(actor, clock.GetUtcNow());
                    if (!inPeriod.Any(x => x.NumericValue == chosen) && !occupied.Contains(InvoiceNumberFormat.Render(series.Configuration, chosen.Value, issueDate).ToUpperInvariant())) break;
                    skipped++;
                }
                if (skipped != 0) decision += $" ExistingNumbersSkipped: pominięto {skipped} zajętych wartości lub pełnych numerów.";
                if (inPeriod.Any(x => x.IssueDate > issueDate)) decision += " EarlierIssueDate: data wcześniejsza od innych dokumentów; przydzielono nowy numer bez bezwzględnej blokady.";
            }
            var reservation = new InvoiceNumberReservation(tenant, invoice.Id, approval.Id, approval.ApprovedRevision, command.InvoiceVersion, seriesId, series.Revision, period, chosen.Value,
                InvoiceNumberFormat.Render(series.Configuration, chosen.Value, issueDate), issueDate, command.OperationId, actor, clock.GetUtcNow(), decision);
            repository.AddReservation(reservation); return reservation;
        }, cancellationToken));

    public async Task<InvoiceNumberReservationResult> MarkUncertainAsync(Guid tenant, Guid invoice, Guid operation, string actor, string reason, CancellationToken cancellationToken = default)
        => Result(await ExecuteAsync(tenant, actor, async ct =>
        {
            var reservation = await LoadAsync(tenant, invoice, operation, ct);
            reservation.Block(actor, clock.GetUtcNow(), reason); return reservation;
        }, cancellationToken));

    public Task<InvoiceNumberReservationResult> ReleaseAsync(Guid tenant, Guid invoice, Guid operation, string actor, CancellationToken cancellationToken = default)
        => ResolveAsync(tenant, invoice, operation, actor, false, cancellationToken);
    public Task<InvoiceNumberReservationResult> FinalizeAsync(Guid tenant, Guid invoice, Guid operation, string actor, CancellationToken cancellationToken = default)
        => ResolveAsync(tenant, invoice, operation, actor, true, cancellationToken);

    private async Task<InvoiceNumberReservationResult> ResolveAsync(Guid tenant, Guid invoiceId, Guid operation, string actor, bool finalize, CancellationToken cancellationToken)
        => Result(await ExecuteAsync(tenant, actor, async ct =>
        {
            var reservation = await LoadAsync(tenant, invoiceId, operation, ct);
            var outcome = await outcomes.GetVerifiedOutcomeAsync(tenant, operation, ct) ?? throw Conflict("OutcomeUnavailable", "Brak wiarygodnego potwierdzenia wyniku wystawienia. Numer pozostaje zajęty.");
            reservation.Resolve(outcome, finalize, actor, clock.GetUtcNow());
            if (finalize)
            {
                var invoice = await repository.GetInvoiceAsync(tenant, invoiceId, ct) ?? throw new KeyNotFoundException();
                invoice.FinalizeNumber(reservation.ApprovalId, reservation.Id, reservation.FullNumber, outcome.KsefNumber!, actor, clock.GetUtcNow());
            }
            return reservation;
        }, cancellationToken));

    private Task<InvoiceNumberReservation> ExecuteAsync(Guid tenant, string actor, Func<CancellationToken, Task<InvoiceNumberReservation>> action, CancellationToken cancellationToken)
        => repository.ExecuteAsync(tenant, async ct =>
        {
            if (!await permissions.HasPermissionAsync(actor, tenant, TenantPermissions.SalesInvoicesIssue, ct)) throw new TenantPermissionDeniedException(TenantPermissions.SalesInvoicesIssue);
            return await action(ct);
        }, cancellationToken);
    private async Task<InvoiceNumberReservation> LoadAsync(Guid tenant, Guid invoice, Guid operation, CancellationToken ct)
    {
        var reservation = await repository.GetOperationAsync(tenant, operation, ct) ?? throw new KeyNotFoundException();
        if (reservation.InvoiceId != invoice) throw new KeyNotFoundException(); return reservation;
    }
    private static byte[] Version(string version)
    {
        Span<byte> bytes = stackalloc byte[8];
        if (version is null || version.Length != 12 || !Convert.TryFromBase64String(version, bytes, out var count) || count != 8) throw Conflict("InvalidVersion", "Wymagana wersja Base64 zawierająca 8 bajtów.");
        return bytes.ToArray();
    }
    private static InvoiceNumberReservationResult Result(InvoiceNumberReservation value) => new(value.Id, value.InvoiceId, value.ApprovalId, value.OperationId, value.SeriesId, value.Period, value.NumericValue, value.FullNumber, value.IssueDate, value.State, value.Decision, Convert.ToBase64String(value.RowVersion));
    private static InvoiceNumberingConflictException Conflict(string code, string message) => new(code, message);
}
