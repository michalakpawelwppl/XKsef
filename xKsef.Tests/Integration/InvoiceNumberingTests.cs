using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.SqlClient;
using xKsef.Application.InvoiceNumbering;
using xKsef.Application.Tenants;
using xKsef.Contracts.InvoiceNumberSeries;
using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.InvoiceNumberSeries;
using xKsef.Domain.SalesInvoices;
using xKsef.Infrastructure.Persistence;
using xKsef.Infrastructure.Persistence.Repositories;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    private static async Task<(Guid Tenant, Browser Browser, InvoiceNumberSeriesDto Series)> NumberingScenarioAsync(Fixture f, NumberSeriesConfigurationDto? configuration = null)
    {
        var (tenant, browser) = await SeriesScenarioAsync(f);
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var shared = await db.TaxEntities.AsNoTracking().SingleOrDefaultAsync(x => x.Nip == "8567346215");
            if (shared is null) await db.TaxEntities.Where(x => db.Tenants.Any(t => t.Id == tenant && t.TaxEntityId == x.Id)).ExecuteUpdateAsync(s => s.SetProperty(x => x.Nip, "8567346215"));
            else await db.Tenants.Where(x => x.Id == tenant).ExecuteUpdateAsync(s => s.SetProperty(x => x.TaxEntityId, shared.Id));
        }
        return (tenant, browser, await CreateSeriesAsync(browser, tenant, configuration));
    }
    private static async Task<SalesInvoiceDto> NumberingInvoiceAsync(Browser browser, Guid tenant, Guid series, DateOnly? date = null)
    {
        var invoice = await CreateSalesAsync(browser, tenant, IssuanceDraft() with { NumberSeriesId = series, PlannedIssueDate = date ?? new DateOnly(2026, 12, 10) });
        using var approved = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{invoice.Id}/approve", new ApproveSalesInvoiceRequest(invoice.Version, invoice.Revision), tenantId: tenant);
        Assert.True(approved.StatusCode == HttpStatusCode.OK, await approved.Content.ReadAsStringAsync());
        return (await approved.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
    }
    private static ReserveInvoiceNumberCommand NumberCommand(SalesInvoiceDto invoice, Guid? operation = null) => new(invoice.Id, invoice.Approval!.Id, operation ?? Guid.NewGuid(), invoice.Version);
    private static async Task<InvoiceNumberReservationResult> NumberActionAsync(Fixture f, Func<IInvoiceNumberingService, Task<InvoiceNumberReservationResult>> action, TestNumberOutcomes? outcomes = null)
    {
        using var scope = f.Scope();
        var service = outcomes is null ? scope.ServiceProvider.GetRequiredService<IInvoiceNumberingService>() : ActivatorUtilities.CreateInstance<InvoiceNumberingService>(scope.ServiceProvider, outcomes);
        return await action(service);
    }
    private static Task<InvoiceNumberReservationResult> ReserveNumberAsync(Fixture f, Guid tenant, ReserveInvoiceNumberCommand command) => NumberActionAsync(f, service => service.ReserveAsync(tenant, f.UserId, command));
    private static async Task<InvoiceNumberReservationResult> ResolveTestNumberAsync(Fixture f, Guid tenant, InvoiceNumberReservationResult reservation, bool accepted)
    {
        var outcomes = new TestNumberOutcomes(); outcomes.Set(tenant, reservation, accepted, f.Clock.GetUtcNow());
        return await NumberActionAsync(f, service => accepted ? service.FinalizeAsync(tenant, reservation.InvoiceId, reservation.OperationId, f.UserId) : service.ReleaseAsync(tenant, reservation.InvoiceId, reservation.OperationId, f.UserId), outcomes);
    }

    [Fact]
    public async Task InvoiceNumberingDraftProposesDefaultAllowsOverrideAndRequiresActiveSeriesBeforeApproval()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        series = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{series.Id}/default", new SetInvoiceNumberSeriesDefaultRequest(series.Version, 0));
        var other = await CreateSeriesAsync(browser, tenant, SeriesConfiguration("FK"));
        var draft = await CreateSalesAsync(browser, tenant, IssuanceDraft()); Assert.Equal(series.Id, draft.Draft.NumberSeriesId);
        using var update = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{draft.Id}", new UpdateSalesInvoiceRequest(draft.Version, draft.Draft with { NumberSeriesId = other.Id }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, update.StatusCode); var chosen = (await update.Content.ReadFromJsonAsync<SalesInvoiceDto>())!; Assert.Equal(other.Id, chosen.Draft.NumberSeriesId);
        other = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{other.Id}/activation", new SetInvoiceNumberSeriesActiveRequest(other.Version, false));
        using var invalid = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{chosen.Id}/approve", new ApproveSalesInvoiceRequest(chosen.Version, chosen.Revision), tenantId: tenant); Assert.Equal(HttpStatusCode.BadRequest, invalid.StatusCode);
        await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{other.Id}/activation", new SetInvoiceNumberSeriesActiveRequest(other.Version, true));
        // Changing invoice inputs invalidates previous tax confirmations; confirm the chosen revision explicitly.
        using var confirm = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{chosen.Id}", new UpdateSalesInvoiceRequest(chosen.Version,
            chosen.Draft with { Fa3 = chosen.Draft.Fa3 with { Qualifications = SalesInvoiceSnapshotTestData.ConfirmedQualificationsDto() } }), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, confirm.StatusCode); chosen = (await confirm.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        using var approve = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{chosen.Id}/approve", new ApproveSalesInvoiceRequest(chosen.Version, chosen.Revision), tenantId: tenant); Assert.Equal(HttpStatusCode.OK, approve.StatusCode);
        var approved = (await approve.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        using var editApproved = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{chosen.Id}", new UpdateSalesInvoiceRequest(approved.Version, chosen.Draft with { NumberSeriesId = series.Id }), tenantId: tenant); Assert.Equal(HttpStatusCode.Conflict, editApproved.StatusCode);
    }

    [Fact]
    public async Task InvoiceNumberingReservationIsBoundToApprovalAndIdempotencyKeyWithoutPublicEndpoint()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var invoice = await NumberingInvoiceAsync(browser, tenant, series.Id); var command = NumberCommand(invoice);
        var first = await ReserveNumberAsync(f, tenant, command); var replay = await ReserveNumberAsync(f, tenant, command);
        Assert.Equal(first, replay); Assert.Equal(1, first.NumericValue); Assert.Equal("FV/000001/2026", first.FullNumber); Assert.Equal(invoice.Approval!.Id, first.ApprovalId);
        await Assert.ThrowsAsync<InvoiceNumberingConflictException>(() => ReserveNumberAsync(f, tenant, command with { OperationId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvoiceNumberingConflictException>(() => ReserveNumberAsync(f, tenant, command with { ApprovalId = Guid.NewGuid() }));
        using var request = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{invoice.Id}/reserve-number", tenantId: tenant); Assert.Equal(HttpStatusCode.NotFound, request.StatusCode);
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(2, (await db.Set<InvoiceNumberCounter>().SingleAsync(x => x.TenantId == tenant)).NextValue);
        var actual = await db.SalesInvoices.SingleAsync(x => x.TenantId == tenant); Assert.Equal(SalesInvoiceStatus.Approved, actual.Status); Assert.Null(actual.InvoiceNumber); Assert.Null(actual.KsefNumber);
    }

    [Fact]
    public async Task InvoiceNumberingConcurrentProcessesAllocateDistinctValuesAndReplayOneOperationOnce()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var commands = new List<ReserveInvoiceNumberCommand>();
        for (var i = 0; i < 6; i++) commands.Add(NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id)));
        var reserved = await Task.WhenAll(commands.Select(command => ReserveNumberAsync(f, tenant, command)));
        Assert.Equal(new long[] { 1, 2, 3, 4, 5, 6 }, reserved.Select(x => x.NumericValue).Order());
        var same = NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id));
        var replays = await Task.WhenAll(ReserveNumberAsync(f, tenant, same), ReserveNumberAsync(f, tenant, same));
        Assert.Equal(replays[0], replays[1]); Assert.Equal(7, replays[0].NumericValue);
    }

    [Fact]
    public async Task InvoiceNumberingUsesIssueYearFreezesStartedCountersAndPreservesFormatSnapshots()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var first = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id, new(2026, 12, 31))));
        series = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{series.Id}", new UpdateInvoiceNumberSeriesRequest(series.Version, series.Configuration with { StartingNumber = 10, Format = "{prefix}/{year}/{number}" }));
        var sameYear = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id, new(2026, 12, 31))));
        var nextYear = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id, new(2027, 1, 1))));
        Assert.Equal(2, sameYear.NumericValue); Assert.Equal(10, nextYear.NumericValue); Assert.Equal(2027, nextYear.Period); Assert.Equal("FV/2026/000002", sameYear.FullNumber);
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, (await db.Set<InvoiceNumberCounter>().SingleAsync(x => x.TenantId == tenant && x.Period == 2026)).StartingNumber);
        Assert.Equal(first.FullNumber, (await db.Set<InvoiceNumberReservation>().SingleAsync(x => x.Id == first.Id)).FullNumber);
        using var reset = await browser.SendAsync(HttpMethod.Put, $"{SeriesPath}/{series.Id}", new UpdateInvoiceNumberSeriesRequest(series.Version, series.Configuration with { ResetMode = 0 }), tenantId: tenant); Assert.Equal(HttpStatusCode.Conflict, reset.StatusCode);
        var continuous = await CreateSeriesAsync(browser, tenant, SeriesConfiguration("CNT") with { Format = "{prefix}/{number}", ResetMode = 0 });
        var a = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, continuous.Id, new(2026, 12, 31))));
        var b = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, continuous.Id, new(2027, 1, 1))));
        Assert.Equal(0, a.Period); Assert.Equal(0, b.Period); Assert.Equal(1, a.NumericValue); Assert.Equal(2, b.NumericValue);
    }

    [Fact]
    public async Task InvoiceNumberingUncertaintyNeverReleasesByTimeoutAndNegativeEvidenceAllowsSafeReuse()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var command = NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id)); var first = await ReserveNumberAsync(f, tenant, command);
        await Assert.ThrowsAsync<InvoiceNumberingConflictException>(() => NumberActionAsync(f, s => s.ReleaseAsync(tenant, first.InvoiceId, first.OperationId, f.UserId)));
        var blocked = await NumberActionAsync(f, s => s.MarkUncertainAsync(tenant, first.InvoiceId, first.OperationId, f.UserId, "Timeout po rozpoczęciu operacji"));
        f.Clock.Advance(TimeSpan.FromDays(365)); Assert.Equal(InvoiceNumberReservationState.Blocked, (await ReserveNumberAsync(f, tenant, command)).State);
        browser = f.Browser(); using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        await Assert.ThrowsAsync<InvoiceNumberingConflictException>(() => NumberActionAsync(f, s => s.ReleaseAsync(tenant, first.InvoiceId, first.OperationId, f.UserId)));
        await Assert.ThrowsAsync<InvoiceNumberingConflictException>(() => ReserveNumberAsync(f, tenant, command with { OperationId = Guid.NewGuid() }));
        var released = await ResolveTestNumberAsync(f, tenant, blocked, false); Assert.Equal(InvoiceNumberReservationState.Released, released.State);
        var reused = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id)));
        Assert.Equal(first.NumericValue, reused.NumericValue); Assert.Contains("ReleasedNumberReused", reused.Decision);
        Assert.Equal(InvoiceNumberReservationState.Released, (await ReserveNumberAsync(f, tenant, command)).State);
        using var scope = f.Scope(); Assert.Equal(2, (await scope.ServiceProvider.GetRequiredService<AppDbContext>().Set<InvoiceNumberCounter>().SingleAsync(x => x.TenantId == tenant)).NextValue);
    }

    [Fact]
    public async Task InvoiceNumberingChronologySkipsUnsafeReuseAndEarlierDateDoesNotBlockNewNumber()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var one = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id)));
        var two = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id)));
        await ResolveTestNumberAsync(f, tenant, two, true); await ResolveTestNumberAsync(f, tenant, one, false);
        var later = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id, new(2026, 12, 11))));
        Assert.Equal(3, later.NumericValue); Assert.Contains("chronologi", later.Decision);
        var earlier = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id, new(2026, 12, 9)))); Assert.Equal(1, earlier.NumericValue);
        var earliest = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id, new(2026, 12, 8))));
        Assert.Equal(4, earliest.NumericValue); Assert.Contains("EarlierIssueDate", earliest.Decision);
    }

    [Fact]
    public async Task InvoiceNumberingReusesLowestReleasedValueAndSkipsFullNumberCollisionAfterFormatChange()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f, SeriesConfiguration() with { Format = "{prefix}/{number}2/{year}", MinimumDigits = 1 });
        var one = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id)));
        Assert.Equal("FV/12/2026", one.FullNumber);
        series = await SeriesResponseAsync(browser, tenant, HttpMethod.Put, $"{SeriesPath}/{series.Id}", new UpdateInvoiceNumberSeriesRequest(series.Version, series.Configuration with { Format = "{prefix}/1{number}/{year}" }));
        var three = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id)));
        Assert.Equal(3, three.NumericValue); Assert.Equal("FV/13/2026", three.FullNumber); Assert.Contains("ExistingNumbersSkipped", three.Decision);
        var four = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id)));
        await ResolveTestNumberAsync(f, tenant, three, false); await ResolveTestNumberAsync(f, tenant, four, false);
        var reused = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id))); Assert.Equal(3, reused.NumericValue);
    }

    [Fact]
    public async Task InvoiceNumberingFinalizationRequiresEvidenceAndIsAtomicIdempotentAndDoesNotMarkIssued()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var reservation = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id)));
        await Assert.ThrowsAsync<InvoiceNumberingConflictException>(() => NumberActionAsync(f, s => s.FinalizeAsync(tenant, reservation.InvoiceId, reservation.OperationId, f.UserId)));
        var outcomes = new TestNumberOutcomes(); outcomes.Set(tenant, reservation, true, f.Clock.GetUtcNow());
        var finalized = await NumberActionAsync(f, s => s.FinalizeAsync(tenant, reservation.InvoiceId, reservation.OperationId, f.UserId), outcomes);
        var replay = await NumberActionAsync(f, s => s.FinalizeAsync(tenant, reservation.InvoiceId, reservation.OperationId, f.UserId), outcomes); Assert.Equal(finalized, replay);
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var invoice = await db.SalesInvoices.AsNoTracking().SingleAsync(x => x.Id == reservation.InvoiceId);
        Assert.Equal(reservation.FullNumber, invoice.InvoiceNumber); Assert.Equal(reservation.Id, invoice.FinalNumberReservationId); Assert.NotNull(invoice.KsefNumber); Assert.Equal(SalesInvoiceStatus.Approved, invoice.Status);
        outcomes.Value = outcomes.Value! with { KsefNumber = "TEST-ONLY-CONTRADICTION" };
        await Assert.ThrowsAsync<InvoiceNumberingConflictException>(() => NumberActionAsync(f, s => s.FinalizeAsync(tenant, reservation.InvoiceId, reservation.OperationId, f.UserId), outcomes));
        await Assert.ThrowsAsync<InvoiceNumberingConflictException>(() => ResolveTestNumberAsync(f, tenant, finalized, false));
    }

    [Fact]
    public async Task InvoiceNumberingMissingSeriesRemainsSavableButNotReadyOrApprovable()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, _) = await NumberingScenarioAsync(f);
        var draft = await CreateSalesAsync(browser, tenant, IssuanceDraft()); Assert.Null(draft.Draft.NumberSeriesId);
        using var validation = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{draft.Id}/validate-issuance", tenantId: tenant);
        var result = (await validation.Content.ReadFromJsonAsync<SalesInvoiceIssuanceValidationDto>())!;
        Assert.False(result.IsReadyForIssuance); Assert.Contains(result.Errors, x => x.Code == "ActiveSeriesRequired");
        using var approve = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{draft.Id}/approve", new ApproveSalesInvoiceRequest(draft.Version, draft.Revision), tenantId: tenant); Assert.Equal(HttpStatusCode.BadRequest, approve.StatusCode);
    }

    [Fact]
    public async Task InvoiceNumberingTenantPermissionsApprovalAndDatabaseLinksCannotBeBypassed()
    {
        await using var f = await Fixture.CreateAsync(); var (a, browser, series) = await NumberingScenarioAsync(f); var (b, _, foreignSeries) = await NumberingScenarioAsync(f);
        var invoice = await NumberingInvoiceAsync(browser, a, series.Id); var command = NumberCommand(invoice);
        await Assert.ThrowsAsync<KeyNotFoundException>(() => ReserveNumberAsync(f, b, command));
        await Assert.ThrowsAsync<TenantPermissionDeniedException>(() => NumberActionAsync(f, s => s.ReserveAsync(a, f.OtherId, command)));
        await Assert.ThrowsAsync<InvoiceNumberingConflictException>(() => ReserveNumberAsync(f, a, command with { ApprovalId = Guid.NewGuid() }));
        await Assert.ThrowsAsync<InvoiceNumberingConflictException>(() => ReserveNumberAsync(f, a, command with { InvoiceVersion = Convert.ToBase64String(new byte[8]) }));
        var draft = await CreateSalesAsync(browser, a, IssuanceDraft() with { NumberSeriesId = series.Id });
        await Assert.ThrowsAsync<InvoiceNumberingConflictException>(() => ReserveNumberAsync(f, a, new(draft.Id, Guid.NewGuid(), Guid.NewGuid(), draft.Version)));
        using var foreignSelection = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{draft.Id}", new UpdateSalesInvoiceRequest(draft.Version, draft.Draft with { NumberSeriesId = foreignSeries.Id }), tenantId: a); Assert.Equal(HttpStatusCode.BadRequest, foreignSelection.StatusCode);
        var reserved = await ReserveNumberAsync(f, a, command);
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SalesInvoices SET NumberSeriesId={foreignSeries.Id} WHERE TenantId={a} AND Id={draft.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SalesInvoices SET InvoiceNumber={reserved.FullNumber} WHERE TenantId={a} AND Id={draft.Id}"));
        await Assert.ThrowsAsync<SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SalesInvoices SET FinalNumberReservationId={reserved.Id},InvoiceNumber={reserved.FullNumber},NormalizedInvoiceNumber={reserved.FullNumber.ToUpperInvariant()},KsefNumber={"TEST-ONLY"} WHERE TenantId={a} AND Id={draft.Id}"));
        await db.UserTenants.Where(x => x.TenantId == a && x.UserId == f.UserId).ExecuteUpdateAsync(s => s.SetProperty(x => x.Role, "User"));
        await Assert.ThrowsAsync<TenantPermissionDeniedException>(() => ReserveNumberAsync(f, a, command));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvoiceNumberingFailureAfterSqlWritesRollsBackWholeCommand(bool finalize)
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var invoice = await NumberingInvoiceAsync(browser, tenant, series.Id); var command = NumberCommand(invoice);
        var reservation = finalize ? await ReserveNumberAsync(f, tenant, command) : null;
        var outcomes = new TestNumberOutcomes(); if (reservation is not null) outcomes.Set(tenant, reservation, true, f.Clock.GetUtcNow());
        using var scope = f.Scope(); var provider = scope.ServiceProvider; var connection = provider.GetRequiredService<AppDbContext>().Database.GetConnectionString();
        await using var failing = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).AddInterceptors(new NumberingSaveFailure()).Options);
        var repository = new InvoiceNumberingRepository(failing, new SalesInvoiceRepository(failing));
        var service = ActivatorUtilities.CreateInstance<InvoiceNumberingService>(provider, repository, outcomes);
        await Assert.ThrowsAsync<IOException>(() => finalize ? service.FinalizeAsync(tenant, invoice.Id, command.OperationId, f.UserId) : service.ReserveAsync(tenant, f.UserId, command));
        using var check = f.Scope(); var db = check.ServiceProvider.GetRequiredService<AppDbContext>();
        var stored = await db.SalesInvoices.AsNoTracking().SingleAsync(x => x.Id == invoice.Id);
        Assert.Null(stored.InvoiceNumber); Assert.Null(stored.KsefNumber); Assert.Equal(invoice.Version, Convert.ToBase64String(stored.RowVersion));
        Assert.Equal(finalize ? 1 : 0, await db.Set<InvoiceNumberCounter>().CountAsync(x => x.TenantId == tenant));
        Assert.Equal(finalize ? 1 : 0, await db.Set<InvoiceNumberReservation>().CountAsync(x => x.TenantId == tenant));
        if (finalize) Assert.Equal(InvoiceNumberReservationState.Active, (await db.Set<InvoiceNumberReservation>().SingleAsync(x => x.Id == reservation!.Id)).State);
        else Assert.Equal(1, (await ReserveNumberAsync(f, tenant, command)).NumericValue);
    }

    [Fact]
    public async Task InvoiceNumberingDuplicateKsefConfirmationRollsBackSecondFinalization()
    {
        await using var f = await Fixture.CreateAsync(); var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var one = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id)));
        var two = await ReserveNumberAsync(f, tenant, NumberCommand(await NumberingInvoiceAsync(browser, tenant, series.Id)));
        await ResolveTestNumberAsync(f, tenant, one, true);
        var outcomes = new TestNumberOutcomes(); outcomes.Set(tenant, two, true, f.Clock.GetUtcNow()); outcomes.Value = outcomes.Value! with { KsefNumber = "TEST-ONLY/" + one.Id };
        await Assert.ThrowsAsync<InvoiceNumberingConflictException>(() => NumberActionAsync(f, s => s.FinalizeAsync(tenant, two.InvoiceId, two.OperationId, f.UserId), outcomes));
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Null((await db.SalesInvoices.SingleAsync(x => x.Id == two.InvoiceId)).InvoiceNumber);
        Assert.Equal(InvoiceNumberReservationState.Active, (await db.Set<InvoiceNumberReservation>().SingleAsync(x => x.Id == two.Id)).State);
    }

    private sealed class NumberingSaveFailure : SaveChangesInterceptor
    {
        public override ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default) => throw new IOException("Test awarii po zapisie SQL, przed commitem numerowania.");
    }

    // Test-only evidence source. Production DI uses UnavailableInvoiceIssuanceOutcomeSource and never invents confirmations.
    private sealed class TestNumberOutcomes : IInvoiceIssuanceOutcomeSource
    {
        public VerifiedInvoiceIssuanceOutcome? Value { get; set; }
        public void Set(Guid tenant, InvoiceNumberReservationResult r, bool accepted, DateTimeOffset now) => Value = new(tenant, r.InvoiceId, r.OperationId, r.Id, r.ApprovalId, r.FullNumber, accepted ? InvoiceIssuanceOutcomeKind.Accepted : InvoiceIssuanceOutcomeKind.DefinitelyNotAccepted, "TEST-ONLY/" + r.Id, accepted ? "TEST-ONLY/" + r.Id : null, now);
        public Task<VerifiedInvoiceIssuanceOutcome?> GetVerifiedOutcomeAsync(Guid tenantId, Guid operationId, CancellationToken cancellationToken) => Task.FromResult(Value?.TenantId == tenantId && Value.OperationId == operationId ? Value : null);
    }
}
