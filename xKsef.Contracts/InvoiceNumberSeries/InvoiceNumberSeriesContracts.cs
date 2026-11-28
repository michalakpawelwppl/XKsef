namespace xKsef.Contracts.InvoiceNumberSeries;

public sealed record NumberSeriesConfigurationDto(string Name, string Prefix, string Format, int MinimumDigits, int ResetMode, long StartingNumber = 1);
public sealed record UpdateInvoiceNumberSeriesRequest(string Version, NumberSeriesConfigurationDto Configuration);
public sealed record SetInvoiceNumberSeriesActiveRequest(string Version, bool IsActive);
public sealed record SetInvoiceNumberSeriesDefaultRequest(string Version, int InvoiceKind, string? DefaultVersion = null);
public sealed record NumberSeriesDefaultDto(int InvoiceKind, string Version);
public sealed record NumberSeriesHistoryDto(int Revision, NumberSeriesConfigurationDto Configuration, bool IsActive, int Change, int? InvoiceKind, string ChangedBy, DateTimeOffset ChangedAt);
public sealed record InvoiceNumberSeriesDto(Guid Id, NumberSeriesConfigurationDto Configuration, bool IsActive, int Revision, string Version, string CreatedBy, DateTimeOffset CreatedAt, string UpdatedBy, DateTimeOffset UpdatedAt, IReadOnlyList<NumberSeriesDefaultDto> Defaults, IReadOnlyList<NumberSeriesHistoryDto> History);
