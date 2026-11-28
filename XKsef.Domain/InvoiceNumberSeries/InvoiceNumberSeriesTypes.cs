using xKsef.Domain.SalesInvoices;

namespace xKsef.Domain.InvoiceNumberSeries;

public enum NumberSeriesResetMode { None = 0, Yearly = 1 }
public enum NumberSeriesChangeKind { Created = 0, ConfigurationUpdated = 1, Activated = 2, Deactivated = 3, DefaultAssigned = 4, DefaultRemoved = 5 }

public sealed record NumberSeriesConfiguration(string Name, string Prefix, string Format, int MinimumDigits, NumberSeriesResetMode ResetMode, long StartingNumber = 1)
{
    public NumberSeriesConfiguration Normalize() => this with { Name = (Name ?? "").Trim(), Prefix = (Prefix ?? "").Trim(), Format = Format ?? "" };
}

public sealed class InvoiceNumberSeriesValidationException(IReadOnlyList<InvoiceValidationIssue> issues) : Exception("Nieprawidłowa konfiguracja serii numeracyjnej.")
{
    public IReadOnlyList<InvoiceValidationIssue> Issues { get; } = issues;
}
public sealed class InvoiceNumberSeriesConflictException(string code = "ConcurrencyConflict", string message = "Konfiguracja serii została zmieniona. Pobierz aktualną wersję.") : Exception(message)
{
    public string Code { get; } = code;
}
