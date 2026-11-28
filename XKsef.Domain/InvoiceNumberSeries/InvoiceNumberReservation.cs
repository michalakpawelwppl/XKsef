namespace xKsef.Domain.InvoiceNumberSeries;

public enum InvoiceNumberReservationState { Active = 0, Finalized = 1, Released = 2, Blocked = 3 }
public enum InvoiceIssuanceOutcomeKind { Accepted = 0, DefinitelyNotAccepted = 1, Uncertain = 2 }

/// <summary>Evidence read from the future issuing process's trusted, durable outcome store, never from a public request.</summary>
public sealed record VerifiedInvoiceIssuanceOutcome(Guid TenantId, Guid InvoiceId, Guid OperationId, Guid ReservationId, Guid ApprovalId, string InvoiceNumber, InvoiceIssuanceOutcomeKind Kind, string EvidenceId, string? KsefNumber, DateTimeOffset ConfirmedAt);

public sealed class InvoiceNumberReservation
{
    private InvoiceNumberReservation() { }
    public InvoiceNumberReservation(Guid tenant, Guid invoice, Guid approval, int approvedRevision, string invoiceVersion, Guid series, int seriesRevision, int period, long value, string number, DateOnly issueDate, Guid operation, string actor, DateTimeOffset now, string decision)
    {
        if (tenant == Guid.Empty || invoice == Guid.Empty || approval == Guid.Empty || series == Guid.Empty || operation == Guid.Empty || approvedRevision <= 0 || seriesRevision <= 0 || value <= 0 || period is < 0 or > 9999 || string.IsNullOrWhiteSpace(number) || number.Length > 100 || string.IsNullOrWhiteSpace(actor) || actor.Length > 450 || string.IsNullOrWhiteSpace(invoiceVersion)) throw new ArgumentException("Nieprawidłowa rezerwacja numeru.");
        Id = Guid.NewGuid(); TenantId = tenant; InvoiceId = invoice; ApprovalId = approval; ApprovedRevision = approvedRevision; InvoiceVersion = invoiceVersion;
        SeriesId = series; SeriesRevision = seriesRevision; Period = period; NumericValue = value; FullNumber = number; NormalizedNumber = number.ToUpperInvariant(); IssueDate = issueDate;
        OperationId = operation; CreatedBy = actor; CreatedAt = now; Decision = decision;
    }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid InvoiceId { get; private set; }
    public Guid ApprovalId { get; private set; }
    public int ApprovedRevision { get; private set; }
    public string InvoiceVersion { get; private set; } = "";
    public Guid SeriesId { get; private set; }
    public int SeriesRevision { get; private set; }
    public int Period { get; private set; }
    public long NumericValue { get; private set; }
    public string FullNumber { get; private set; } = "";
    public string NormalizedNumber { get; private set; } = "";
    public DateOnly IssueDate { get; private set; }
    public Guid OperationId { get; private set; }
    public InvoiceNumberReservationState State { get; private set; }
    public bool HoldsNumber { get; private set; } = true;
    public string CreatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public string Decision { get; private set; } = "";
    public DateTimeOffset? BlockedAt { get; private set; }
    public string? BlockedBy { get; private set; }
    public string? UncertaintyReason { get; private set; }
    public DateTimeOffset? ResolvedAt { get; private set; }
    public string? ResolvedBy { get; private set; }
    public string? EvidenceId { get; private set; }
    public DateTimeOffset? ConfirmedAt { get; private set; }
    public string? KsefNumber { get; private set; }
    public byte[] RowVersion { get; private set; } = [];

    public void Block(string actor, DateTimeOffset now, string reason)
    {
        if (State == InvoiceNumberReservationState.Blocked) return;
        if (State != InvoiceNumberReservationState.Active) throw Conflict();
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450 || string.IsNullOrWhiteSpace(reason) || reason.Length > 1000) throw new ArgumentException("Podaj użytkownika i przyczynę niepewności.");
        State = InvoiceNumberReservationState.Blocked; BlockedAt = now; BlockedBy = actor; UncertaintyReason = reason;
    }
    public void Resolve(VerifiedInvoiceIssuanceOutcome outcome, bool finalize, string actor, DateTimeOffset now)
    {
        if (outcome.TenantId != TenantId || outcome.InvoiceId != InvoiceId || outcome.OperationId != OperationId || outcome.ReservationId != Id || outcome.ApprovalId != ApprovalId || outcome.InvoiceNumber != FullNumber) throw Conflict();
        if (outcome.Kind != (finalize ? InvoiceIssuanceOutcomeKind.Accepted : InvoiceIssuanceOutcomeKind.DefinitelyNotAccepted)) throw Conflict();
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450 || string.IsNullOrWhiteSpace(outcome.EvidenceId) || outcome.EvidenceId.Length > 256 || outcome.ConfirmedAt == default || (finalize && (string.IsNullOrWhiteSpace(outcome.KsefNumber) || outcome.KsefNumber.Length > 100)) || (!finalize && outcome.KsefNumber is not null)) throw Conflict();
        var target = finalize ? InvoiceNumberReservationState.Finalized : InvoiceNumberReservationState.Released;
        if (State is InvoiceNumberReservationState.Finalized or InvoiceNumberReservationState.Released)
        {
            if (State == target && EvidenceId == outcome.EvidenceId && KsefNumber == outcome.KsefNumber && ConfirmedAt == outcome.ConfirmedAt) return;
            throw Conflict();
        }
        State = target; HoldsNumber = finalize; EvidenceId = outcome.EvidenceId; KsefNumber = outcome.KsefNumber; ConfirmedAt = outcome.ConfirmedAt;
        ResolvedAt = now; ResolvedBy = actor;
    }
    private static InvoiceNumberingConflictException Conflict() => new("ContradictoryOutcome", "Stan rezerwacji lub potwierdzenie nie odpowiada tej operacji i fakturze.");
}
