using xKsef.Application.Tenants;
using xKsef.Contracts.InvoiceNumberSeries;
using xKsef.Domain.InvoiceNumberSeries;
using xKsef.Domain.SalesInvoices;
using NumberSeries = xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries;

namespace xKsef.Application.InvoiceNumbering;

public interface IInvoiceNumberSeriesService
{
    Task<InvoiceNumberSeriesDto> CreateAsync(Guid tenant, string actor, NumberSeriesConfigurationDto configuration, CancellationToken cancellationToken = default);
    Task<InvoiceNumberSeriesDto> GetAsync(Guid tenant, Guid id, string actor, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<InvoiceNumberSeriesDto>> ListAsync(Guid tenant, string actor, CancellationToken cancellationToken = default);
    Task<InvoiceNumberSeriesDto> UpdateAsync(Guid tenant, Guid id, string actor, UpdateInvoiceNumberSeriesRequest request, CancellationToken cancellationToken = default);
    Task<InvoiceNumberSeriesDto> SetActiveAsync(Guid tenant, Guid id, string actor, SetInvoiceNumberSeriesActiveRequest request, CancellationToken cancellationToken = default);
    Task<InvoiceNumberSeriesDto> SetDefaultAsync(Guid tenant, Guid id, string actor, SetInvoiceNumberSeriesDefaultRequest request, CancellationToken cancellationToken = default);
}

public sealed class InvoiceNumberSeriesService(IInvoiceNumberSeriesRepository repository, ITenantPermissionService permissions, TimeProvider clock) : IInvoiceNumberSeriesService
{
    public async Task<InvoiceNumberSeriesDto> CreateAsync(Guid tenant, string actor, NumberSeriesConfigurationDto configuration, CancellationToken cancellationToken = default)
        => ToDto(await MutateAsync(tenant, actor, async ct =>
        {
            var series = NumberSeries.Create(tenant, Configuration(configuration), actor, clock.GetUtcNow());
            await CheckNamespaceAsync(series, series.Configuration, ct);
            repository.Add(series); return series;
        }, cancellationToken));

    public async Task<InvoiceNumberSeriesDto> GetAsync(Guid tenant, Guid id, string actor, CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(tenant, actor, cancellationToken);
        return ToDto(await repository.GetAsync(tenant, id, false, cancellationToken) ?? throw new KeyNotFoundException());
    }
    public async Task<IReadOnlyList<InvoiceNumberSeriesDto>> ListAsync(Guid tenant, string actor, CancellationToken cancellationToken = default)
    {
        await RequirePermissionAsync(tenant, actor, cancellationToken);
        return (await repository.ListAsync(tenant, cancellationToken)).Select(ToDto).ToArray();
    }
    public async Task<InvoiceNumberSeriesDto> UpdateAsync(Guid tenant, Guid id, string actor, UpdateInvoiceNumberSeriesRequest request, CancellationToken cancellationToken = default)
        => ToDto(await MutateAsync(tenant, actor, async ct =>
        {
            var series = await LoadAsync(tenant, id, request.Version, ct);
            var configuration = Configuration(request.Configuration);
            if (configuration.ResetMode != series.Configuration.ResetMode && await repository.HasCountersAsync(tenant, id, ct))
                throw new InvoiceNumberSeriesConflictException("ResetModeLocked", "Nie można zmienić sposobu resetowania po rozpoczęciu numeracji serii.");
            await CheckNamespaceAsync(series, configuration, ct);
            series.UpdateConfiguration(configuration, actor, clock.GetUtcNow()); return series;
        }, cancellationToken));

    public async Task<InvoiceNumberSeriesDto> SetActiveAsync(Guid tenant, Guid id, string actor, SetInvoiceNumberSeriesActiveRequest request, CancellationToken cancellationToken = default)
        => ToDto(await MutateAsync(tenant, actor, async ct =>
        {
            var series = await LoadAsync(tenant, id, request.Version, ct);
            if (!request.IsActive) repository.RemoveDefaults(series.Defaults.ToArray());
            series.SetActive(request.IsActive, actor, clock.GetUtcNow()); return series;
        }, cancellationToken));

    public async Task<InvoiceNumberSeriesDto> SetDefaultAsync(Guid tenant, Guid id, string actor, SetInvoiceNumberSeriesDefaultRequest request, CancellationToken cancellationToken = default)
        => ToDto(await MutateAsync(tenant, actor, async ct =>
        {
            var series = await LoadAsync(tenant, id, request.Version, ct);
            var kind = (SalesInvoiceKind)request.InvoiceKind;
            if (!InvoiceNumberSeriesDefault.IsSupportedKind(kind) || !series.IsActive) throw Invalid("invoiceKind", "InvalidDefault", "Wybierz poprawny typ faktury i aktywną serię.");
            var assignment = await repository.GetDefaultAsync(tenant, kind, ct);
            if (assignment is null)
            {
                if (request.DefaultVersion is not null) throw new InvoiceNumberSeriesConflictException();
                repository.AddDefault(new(tenant, kind, id));
            }
            else
            {
                if (request.DefaultVersion is null) throw new InvoiceNumberSeriesConflictException();
                if (!assignment.RowVersion.SequenceEqual(Version(request.DefaultVersion))) throw new InvoiceNumberSeriesConflictException();
                if (assignment.SeriesId == id) return series;
                var previous = await repository.GetAsync(tenant, assignment.SeriesId, true, ct) ?? throw new KeyNotFoundException();
                previous.RecordDefaultChange(false, kind, actor, clock.GetUtcNow());
                assignment.Assign(id);
            }
            series.RecordDefaultChange(true, kind, actor, clock.GetUtcNow()); return series;
        }, cancellationToken));

    private Task<NumberSeries> MutateAsync(Guid tenant, string actor, Func<CancellationToken, Task<NumberSeries>> action, CancellationToken cancellationToken)
        => repository.ExecuteAsync(tenant, async ct => { await RequirePermissionAsync(tenant, actor, ct); return await action(ct); }, cancellationToken);

    private async Task RequirePermissionAsync(Guid tenant, string actor, CancellationToken cancellationToken)
    {
        if (!await permissions.HasPermissionAsync(actor, tenant, TenantPermissions.InvoiceNumberSeriesManage, cancellationToken))
            throw new TenantPermissionDeniedException(TenantPermissions.InvoiceNumberSeriesManage);
    }
    private async Task<NumberSeries> LoadAsync(Guid tenant, Guid id, string? version, CancellationToken cancellationToken)
    {
        var series = await repository.GetAsync(tenant, id, true, cancellationToken) ?? throw new KeyNotFoundException();
        if (!series.RowVersion.SequenceEqual(Version(version))) throw new InvoiceNumberSeriesConflictException();
        return series;
    }
    private async Task CheckNamespaceAsync(NumberSeries series, NumberSeriesConfiguration configuration, CancellationToken cancellationToken)
    {
        InvoiceNumberFormat.Validate(configuration);
        if (await repository.NameExistsAsync(series.TenantId, series.Id, configuration.Name.ToUpperInvariant(), cancellationToken))
            throw new InvoiceNumberSeriesConflictException("NameAlreadyExists", "Nazwa serii jest już używana w tym tenantcie.");
        foreach (var previous in await repository.OtherConfigurationsAsync(series.TenantId, series.Id, cancellationToken))
        {
            var collision = InvoiceNumberFormat.FindCollision(configuration, previous);
            if (collision is not null) throw new InvoiceNumberSeriesConflictException("FormatCollision", $"Format koliduje z konfiguracją innej serii tego tenanta, np. dla numeru {collision}.");
        }
    }
    private static byte[] Version(string? value)
    {
        if (value is not null)
        {
            Span<byte> bytes = stackalloc byte[8];
            if (Convert.TryFromBase64String(value, bytes, out var count) && count == 8) return bytes.ToArray();
        }
        throw Invalid("version", "InvalidVersion", "Wymagana wersja Base64 zawierająca 8 bajtów.");
    }
    private static InvoiceNumberSeriesValidationException Invalid(string path, string code, string message) => new([new(path, code, message)]);
    private static NumberSeriesConfiguration Configuration(NumberSeriesConfigurationDto dto)
        => dto is null ? throw Invalid("configuration", "Required", "Podaj konfigurację serii.") : new NumberSeriesConfiguration(dto.Name, dto.Prefix, dto.Format, dto.MinimumDigits, (NumberSeriesResetMode)dto.ResetMode, dto.StartingNumber).Normalize();
    private static NumberSeriesConfigurationDto Configuration(NumberSeriesConfiguration value) => new(value.Name, value.Prefix, value.Format, value.MinimumDigits, (int)value.ResetMode, value.StartingNumber);
    private static InvoiceNumberSeriesDto ToDto(NumberSeries series) => new(series.Id, Configuration(series.Configuration), series.IsActive, series.Revision, Convert.ToBase64String(series.RowVersion), series.CreatedBy, series.CreatedAt, series.UpdatedBy, series.UpdatedAt,
        series.Defaults.OrderBy(x => x.InvoiceKind).Select(x => new NumberSeriesDefaultDto((int)x.InvoiceKind, Convert.ToBase64String(x.RowVersion))).ToArray(),
        series.History.OrderBy(x => x.Revision).Select(x => new NumberSeriesHistoryDto(x.Revision, Configuration(x.Configuration), x.IsActive, (int)x.Change, x.InvoiceKind is null ? null : (int)x.InvoiceKind, x.ChangedBy, x.ChangedAt)).ToArray());
}
