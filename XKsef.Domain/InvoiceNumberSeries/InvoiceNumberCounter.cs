namespace xKsef.Domain.InvoiceNumberSeries;

public sealed class InvoiceNumberCounter
{
    private InvoiceNumberCounter() { }
    public InvoiceNumberCounter(Guid tenant, Guid series, int period, long startingNumber, string actor, DateTimeOffset now)
    {
        if (tenant == Guid.Empty || series == Guid.Empty || period is < 0 or > 9999 || startingNumber <= 0 || string.IsNullOrWhiteSpace(actor)) throw new ArgumentException("Nieprawidłowe dane licznika.");
        TenantId = tenant; SeriesId = series; Period = period; StartingNumber = startingNumber; NextValue = startingNumber;
        CreatedBy = actor; UpdatedBy = actor; CreatedAt = now; UpdatedAt = now;
    }
    public Guid TenantId { get; private set; }
    public Guid SeriesId { get; private set; }
    public int Period { get; private set; }
    public long StartingNumber { get; private set; }
    public long? NextValue { get; private set; }
    public byte[] RowVersion { get; private set; } = [];
    public string CreatedBy { get; private set; } = "";
    public string UpdatedBy { get; private set; } = "";
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public long TakeNext(string actor, DateTimeOffset now)
    {
        if (string.IsNullOrWhiteSpace(actor) || actor.Length > 450) throw new ArgumentException("Nieprawidłowy użytkownik.");
        var value = NextValue ?? throw new InvoiceNumberingConflictException("CounterExhausted", "Wyczerpano zakres licznika.");
        NextValue = value == long.MaxValue ? null : value + 1; UpdatedBy = actor; UpdatedAt = now; return value;
    }
    public static int PeriodFor(NumberSeriesResetMode reset, DateOnly issueDate) => reset switch
    {
        NumberSeriesResetMode.None => 0,
        NumberSeriesResetMode.Yearly => issueDate.Year,
        _ => throw new ArgumentOutOfRangeException(nameof(reset))
    };
}

public sealed class InvoiceNumberingConflictException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}
