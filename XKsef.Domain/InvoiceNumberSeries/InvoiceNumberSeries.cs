using xKsef.Domain.SalesInvoices;

namespace xKsef.Domain.InvoiceNumberSeries;

public sealed class InvoiceNumberSeries
{
    private readonly List<InvoiceNumberSeriesRevision> _history = [];
    private readonly List<InvoiceNumberSeriesDefault> _defaults = [];
    private InvoiceNumberSeries() { }
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public NumberSeriesConfiguration Configuration { get; private set; } = null!;
    public string NormalizedName { get; private set; } = "";
    public bool IsActive { get; private set; } = true;
    public int Revision { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public string CreatedBy { get; private set; } = "";
    public string UpdatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public IReadOnlyList<InvoiceNumberSeriesRevision> History => _history.AsReadOnly();
    public IReadOnlyList<InvoiceNumberSeriesDefault> Defaults => _defaults.AsReadOnly();

    public static InvoiceNumberSeries Create(Guid tenantId, NumberSeriesConfiguration configuration, string actor, DateTimeOffset now)
    {
        if (tenantId == Guid.Empty) throw new ArgumentException("Nieprawidłowy tenant.");
        var normalized = configuration.Normalize(); InvoiceNumberFormat.Validate(normalized);
        var series = new InvoiceNumberSeries { Id = Guid.NewGuid(), TenantId = tenantId, Configuration = normalized, NormalizedName = normalized.Name.ToUpperInvariant(), CreatedBy = actor, CreatedAt = now };
        series.RecordChange(NumberSeriesChangeKind.Created, actor, now);
        return series;
    }
    public void UpdateConfiguration(NumberSeriesConfiguration configuration, string actor, DateTimeOffset now)
    {
        var normalized = configuration.Normalize(); InvoiceNumberFormat.Validate(normalized);
        Configuration = normalized; NormalizedName = normalized.Name.ToUpperInvariant();
        // Existing counters retain their immutable StartingNumber; configuration supplies only new period initialization.
        // Format changes have no counter side effects. Counters are keyed by tenant/series/period, never invoice kind.
        RecordChange(NumberSeriesChangeKind.ConfigurationUpdated, actor, now);
    }
    public void SetActive(bool active, string actor, DateTimeOffset now)
    {
        if (IsActive == active) return;
        IsActive = active; RecordChange(active ? NumberSeriesChangeKind.Activated : NumberSeriesChangeKind.Deactivated, actor, now);
    }
    public void RecordDefaultChange(bool assigned, SalesInvoiceKind kind, string actor, DateTimeOffset now)
    {
        if (!InvoiceNumberSeriesDefault.IsSupportedKind(kind) || assigned && !IsActive) throw new InvoiceNumberSeriesValidationException([new("invoiceKind", "InvalidDefault", "Seria domyślna musi być aktywna, a typ faktury poprawny.")]);
        RecordChange(assigned ? NumberSeriesChangeKind.DefaultAssigned : NumberSeriesChangeKind.DefaultRemoved, actor, now, kind);
    }
    private void RecordChange(NumberSeriesChangeKind change, string actor, DateTimeOffset now, SalesInvoiceKind? kind = null)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450) throw new ArgumentException("Nieprawidłowy użytkownik.");
        Revision = checked(Revision + 1); UpdatedBy = actor; UpdatedAt = now;
        _history.Add(new(TenantId, Id, Revision, Configuration, IsActive, change, kind, actor, now));
    }
}

public sealed class InvoiceNumberSeriesRevision
{
    private InvoiceNumberSeriesRevision() { }
    internal InvoiceNumberSeriesRevision(Guid tenant, Guid series, int revision, NumberSeriesConfiguration configuration, bool active, NumberSeriesChangeKind change, SalesInvoiceKind? kind, string actor, DateTimeOffset now)
    { TenantId = tenant; SeriesId = series; Revision = revision; Configuration = configuration; IsActive = active; Change = change; InvoiceKind = kind; ChangedBy = actor; ChangedAt = now; }
    public Guid TenantId { get; private set; }
    public Guid SeriesId { get; private set; }
    public int Revision { get; private set; }
    public NumberSeriesConfiguration Configuration { get; private set; } = null!;
    public bool IsActive { get; private set; }
    public NumberSeriesChangeKind Change { get; private set; }
    public SalesInvoiceKind? InvoiceKind { get; private set; }
    public string ChangedBy { get; private set; } = "";
    public DateTimeOffset ChangedAt { get; private set; }
}

public sealed class InvoiceNumberSeriesDefault
{
    private InvoiceNumberSeriesDefault() { }
    public InvoiceNumberSeriesDefault(Guid tenant, SalesInvoiceKind kind, Guid series) { TenantId = tenant; InvoiceKind = kind; SeriesId = series; }
    public Guid TenantId { get; private set; }
    public SalesInvoiceKind InvoiceKind { get; private set; }
    public Guid SeriesId { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public void Assign(Guid series) => SeriesId = series;
    public static bool IsSupportedKind(SalesInvoiceKind kind) => kind is SalesInvoiceKind.Vat or SalesInvoiceKind.Advance or SalesInvoiceKind.Final or SalesInvoiceKind.Correction;
}
