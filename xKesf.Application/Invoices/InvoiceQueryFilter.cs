using xKsef.Contracts.Invoices;

namespace xKsef.Application.Invoices;

public sealed class InvoiceQueryFilter
{
    public DateOnly From { get; }
    public DateOnly To { get; }
    public InvoiceDirection Direction { get; }
    public InvoiceDateKind DateKind { get; }
    public DateTimeOffset? FromUtc { get; }
    public DateTimeOffset? ToUtcExclusive { get; }

    private InvoiceQueryFilter(DateOnly from, DateOnly to, InvoiceDirection direction, InvoiceDateKind dateKind, DateTimeOffset? fromUtc, DateTimeOffset? toUtcExclusive)
    {
        From = from;
        To = to;
        Direction = direction;
        DateKind = dateKind;
        FromUtc = fromUtc;
        ToUtcExclusive = toUtcExclusive;
    }

    public static InvoiceQueryFilter Create(DateOnly from, DateOnly to, InvoiceDirection direction, InvoiceDateKind dateKind = InvoiceDateKind.IssueDate, string? timeZoneId = null)
    {
        if (from > to) throw new ArgumentException("Data początkowa nie może być późniejsza niż końcowa.");
        if (!Enum.IsDefined(direction)) throw new ArgumentException("Nieprawidłowy kierunek faktur.", nameof(direction));
        if (!Enum.IsDefined(dateKind)) throw new ArgumentException("Nieprawidłowy rodzaj daty.", nameof(dateKind));
        if (dateKind == InvoiceDateKind.IssueDate)
            return new InvoiceQueryFilter(from, to, direction, dateKind, null, null);

        if (string.IsNullOrWhiteSpace(timeZoneId)) throw new ArgumentException("Filtrowanie po dacie przyjęcia do KSeF wymaga strefy czasowej.", nameof(timeZoneId));
        if (to == DateOnly.MaxValue) throw new ArgumentException("Nie można wyznaczyć końca zakresu dla ostatniego dnia roku 9999.", nameof(to));
        TimeZoneInfo timeZone;
        try
        {
            timeZone = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
        {
            throw new ArgumentException("Nieprawidłowa lub niedostępna strefa czasowa.", nameof(timeZoneId), ex);
        }
        DateTimeOffset start = MidnightUtc(from, timeZone);
        DateTimeOffset end = MidnightUtc(to.AddDays(1), timeZone);
        if (start >= end) throw new ArgumentException("Zakres dat nie wyznacza poprawnego przedziału czasu.");
        return new InvoiceQueryFilter(from, to, direction, dateKind, start, end);
    }

    private static DateTimeOffset MidnightUtc(DateOnly day, TimeZoneInfo timeZone)
    {
        DateTime local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        if (timeZone.IsInvalidTime(local) || timeZone.IsAmbiguousTime(local))
            throw new ArgumentException("Północ na granicy zakresu jest nieistniejąca lub niejednoznaczna w wybranej strefie czasowej.");
        // Construct the boundary with its actual offset to reject out-of-range instants instead of clamping them.
        return new DateTimeOffset(local, timeZone.GetUtcOffset(local)).ToUniversalTime();
    }
}
