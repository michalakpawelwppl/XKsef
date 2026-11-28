using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Application.Contractors;
using xKsef.Application.SalesInvoices;
using xKsef.Contracts.Authentication;
using xKsef.Contracts.SalesInvoices;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;
using xKsef.Infrastructure.Persistence;
using xKsef.Infrastructure.Persistence.Repositories;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    private static SalesInvoiceDraftDto SalesDraft() => new()
    {
        PlannedIssueDate = new DateOnly(2026, 12, 1), PdfLanguage = 2, Notes = "Notatka",
        Lines = [new() { UnitPrice = new(100.12345678m), Quantity = new(1.25m), Description = "Towar", Unit = "kg" }],
        Details = new() { PaymentMethod = 6, PaymentTerms = [new(new DateOnly(2026, 12, 15))], BankAccounts = [new("PL61109010140000071219812874", "WBKPPLPP")] }
    };

    private static async Task<SalesInvoiceDto> CreateSalesAsync(Browser browser, Guid tenant, SalesInvoiceDraftDto? draft = null)
    {
        using var response = await browser.SendAsync(HttpMethod.Post, "/api/sales-invoices", draft ?? SalesDraft(), tenantId: tenant);
        Assert.True(response.StatusCode == HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
        Assert.NotNull(response.Headers.Location);
        return (await response.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
    }

    [Fact]
    public async Task SalesInvoiceSqlAndApiRoundTripPreservesInputsIdsDetailsAndRecalculatesMethods()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, tenant);
        Assert.Equal(0, created.Status); Assert.True(created.CalculationComplete); Assert.Equal(1, created.Revision);
        Assert.Equal(125.15m, created.Totals.Net!.Value.Value); Assert.Equal(8, Convert.FromBase64String(created.Version).Length);
        var id = created.Draft.Lines[0].Id; var firstVersion = created.Version;
        foreach (var method in new[] { 0, 1, 2, 3 })
        {
            var draft = created.Draft with { VatMethod = method, GlobalDiscountBasis = 1, GlobalDiscount = new(0, new(10m)) };
            using var response = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(created.Version, draft), tenantId: tenant);
            Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
            created = (await response.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
            Assert.Equal(1, created.Draft.GlobalDiscountBasis);
            Assert.Equal(100.12345678m, created.Draft.Lines[0].UnitPrice!.Value.Value); Assert.Equal(id, created.Draft.Lines[0].Id);
        }
        Assert.NotEqual(firstVersion, created.Version);
        using var get = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        var read = (await get.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(created.Version, read.Version); Assert.Equal(created.Totals, read.Totals);
        Assert.Equal("PL61109010140000071219812874", read.Draft.Details.BankAccounts[0].Number);
        Assert.Equal(2, read.Draft.PdfLanguage);
        using var recalc = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/recalculate", new RecalculateSalesInvoiceRequest(created.Version), tenantId: tenant);
        Assert.Equal(HttpStatusCode.OK, recalc.StatusCode);
        var recalculated = (await recalc.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(read.Totals, recalculated.Totals); Assert.Equal(read.Revision + 1, recalculated.Revision);
        using var list = await browser.SendAsync(HttpMethod.Get, "/api/sales-invoices?page=1&pageSize=1", tenantId: tenant);
        var page = (await list.Content.ReadFromJsonAsync<SalesInvoiceListDto>())!;
        Assert.Equal(1, page.TotalCount); Assert.Equal(created.Id, Assert.Single(page.Items).Id);
    }

    [Fact]
    public async Task SalesInvoiceMissingFieldsCanBeSavedWithoutInventingZeroTotals()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var draft = await CreateSalesAsync(browser, tenant, new());
        Assert.False(draft.CalculationComplete); Assert.Null(draft.Totals.Gross); Assert.NotEmpty(draft.MissingData);
        Assert.NotNull(draft.Draft.Seller); Assert.Null(draft.Draft.Buyer);
    }

    [Fact]
    public async Task SalesInvoiceRateSnapshotsPreserveTwelveDecimalsAndManualHistory()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var data = SalesDraft() with { Currency = "EUR", ExchangeRates = [new() { Rate = new(4.123456789012m), RateDate = new DateOnly(2026, 11, 26), SourceReference = "Kurs ręczny" }] };
        var created = await CreateSalesAsync(browser, tenant, data);
        var originalRate = Assert.Single(created.ExchangeRateHistory).Snapshot;
        var update = created.Draft with { ExchangeRates = [originalRate with { Rate = new(4.223456789012m), IsManualOverride = true, OverrideReason = "Zmiana użytkownika", OriginalRate = originalRate.Rate }] };
        using var response = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(created.Version, update), tenantId: tenant);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        var updated = (await response.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(2, updated.ExchangeRateHistory.Count);
        Assert.Equal(4.123456789012m, updated.ExchangeRateHistory.Single(x => !x.IsCurrent).Snapshot.Rate.Value);
        Assert.Equal(4.223456789012m, updated.Draft.ExchangeRates[0].Rate.Value);
        Assert.Equal(originalRate.Id, updated.Draft.ExchangeRates[0].OriginalSnapshotId);
        using var recalc = await browser.SendAsync(HttpMethod.Post, $"/api/sales-invoices/{created.Id}/recalculate", new RecalculateSalesInvoiceRequest(updated.Version), tenantId: tenant);
        var recalculated = (await recalc.Content.ReadFromJsonAsync<SalesInvoiceDto>())!;
        Assert.Equal(2, recalculated.ExchangeRateHistory.Count);
    }

    [Fact]
    public async Task SalesInvoiceTenantBoundaryProtectsReadUpdateListAndSourceContractors()
    {
        await using var f = await Fixture.CreateAsync(); var a = await f.CreateTenantAsync(); var b = await f.CreateTenantAsync(); var foreign = await f.CreateTenantAsync(f.OtherId);
        var browser = f.Browser(); using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var own = await CreateSalesAsync(browser, a);
        using var read = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{own.Id}", tenantId: b);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);
        using var update = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{own.Id}", new UpdateSalesInvoiceRequest(own.Version, own.Draft), tenantId: b);
        Assert.Equal(HttpStatusCode.NotFound, update.StatusCode);
        using var denied = await browser.SendAsync(HttpMethod.Get, "/api/sales-invoices", tenantId: foreign);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        using var list = await browser.SendAsync(HttpMethod.Get, "/api/sales-invoices", tenantId: b);
        Assert.Empty((await list.Content.ReadFromJsonAsync<SalesInvoiceListDto>())!.Items);
        using var invalidLine = await browser.SendAsync(HttpMethod.Post, "/api/sales-invoices", own.Draft, tenantId: b);
        Assert.Equal(HttpStatusCode.BadRequest, invalidLine.StatusCode);
        Guid contractor;
        using (var scope = f.Scope()) contractor = await scope.ServiceProvider.GetRequiredService<IContractorService>().CreateAsync(b, new() { FullName = "Obcy", ShortName = "O" });
        using var wrongSource = await browser.SendAsync(HttpMethod.Post, "/api/sales-invoices", SalesDraft() with { Buyer = new() { SourceContractorId = contractor } }, tenantId: a);
        Assert.Equal(HttpStatusCode.BadRequest, wrongSource.StatusCode);
    }

    [Fact]
    public async Task SalesInvoiceSnapshotsDoNotRefreshWhenContractorChanges()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); Guid contractorId;
        using (var scope = f.Scope()) contractorId = await scope.ServiceProvider.GetRequiredService<IContractorService>().CreateAsync(tenant, new() { FullName = "Pierwotna nazwa", ShortName = "P", Nip = "8567346215" });
        var browser = f.Browser(); using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        var created = await CreateSalesAsync(browser, tenant, SalesDraft() with { Buyer = new() { SourceContractorId = contractorId } });
        Assert.Equal("Pierwotna nazwa", created.Draft.Buyer!.Name);
        using (var scope = f.Scope()) await scope.ServiceProvider.GetRequiredService<IContractorService>().UpdateAsync(tenant, contractorId, new() { FullName = "Nowa nazwa", ShortName = "N", IsActive = true, Addresses = [] });
        using var get = await browser.SendAsync(HttpMethod.Get, $"/api/sales-invoices/{created.Id}", tenantId: tenant);
        Assert.Equal("Pierwotna nazwa", (await get.Content.ReadFromJsonAsync<SalesInvoiceDto>())!.Draft.Buyer!.Name);
        using var update = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(created.Version, created.Draft), tenantId: tenant);
        Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());
        Assert.Equal("Pierwotna nazwa", (await update.Content.ReadFromJsonAsync<SalesInvoiceDto>())!.Draft.Buyer!.Name);
    }

    [Fact]
    public async Task SalesInvoiceCookiesRequireCsrfAndJwtRetainsExistingAuthentication()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var anonymous = await browser.SendAsync(HttpMethod.Get, "/api/sales-invoices", tenantId: tenant); Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var login = await browser.LoginAsync(); browser.Token = null;
        using var denied = await browser.SendAsync(HttpMethod.Post, "/api/sales-invoices", SalesDraft(), tenantId: tenant); Assert.Equal(HttpStatusCode.BadRequest, denied.StatusCode);
        using var tokenResponse = await f.Client.PostAsJsonAsync("/api/auth/login", new { Login = f.Login, Password });
        Assert.Equal(HttpStatusCode.OK, tokenResponse.StatusCode);
        var token = (await tokenResponse.Content.ReadFromJsonAsync<LoginResponse>())!;
        using var jwt = await browser.SendAsync(HttpMethod.Post, "/api/sales-invoices", SalesDraft(), "Bearer " + token.AccessToken, tenant);
        Assert.True(jwt.StatusCode == HttpStatusCode.Created, await jwt.Content.ReadAsStringAsync());
    }

    [Theory]
    [InlineData("{\"currency\":null}")]
    [InlineData("{\"kind\":1}")]
    [InlineData("{\"vatMethod\":99}")]
    [InlineData("{\"lines\":null}")]
    [InlineData("{\"lines\":[null]}")]
    [InlineData("{\"lines\":[{\"quantity\":\"1.0000001\"}]}")]
    [InlineData("{\"lines\":[{\"unitPrice\":\"0.000000001\"}]}")]
    [InlineData("{\"lines\":[{\"unitPrice\":10.1}]}")]
    [InlineData("{\"lines\":[{\"unitPrice\":\"1e2\"}]}")]
    [InlineData("{\"lines\":[{\"unitPrice\":\"10000000000000.123456789012345\"}]}")]
    [InlineData("{\"details\":null}")]
    [InlineData("{\"details\":{\"paymentTerms\":[null]}}")]
    [InlineData("{\"globalDiscount\":{\"kind\":0,\"value\":\"101\"}}")]
    [InlineData("{\"status\":3}")]
    [InlineData("{\"tenantId\":\"00000000-0000-0000-0000-000000000001\"}")]
    public async Task SalesInvoiceInvalidRequestsAreRejectedWithoutWriting(string json)
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync();
        using var body = JsonDocument.Parse(json);
        using var response = await browser.SendAsync(HttpMethod.Post, "/api/sales-invoices", body.RootElement, tenantId: tenant);
        Assert.True(response.StatusCode == HttpStatusCode.BadRequest, await response.Content.ReadAsStringAsync());
        using var scope = f.Scope(); Assert.False(await scope.ServiceProvider.GetRequiredService<AppDbContext>().SalesInvoices.AnyAsync(x => x.TenantId == tenant));
    }

    [Fact]
    public async Task SalesInvoiceStaleVersionsAndNonDraftWritesAreRejected()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync(); var created = await CreateSalesAsync(browser, tenant);
        using var updated = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(created.Version, created.Draft), tenantId: tenant);
        Assert.True(updated.IsSuccessStatusCode, await updated.Content.ReadAsStringAsync());
        using var stale = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(created.Version, created.Draft), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Conflict, stale.StatusCode);
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.SalesInvoices.Where(x => x.Id == created.Id && x.TenantId == tenant).ExecuteUpdateAsync(setters => setters.SetProperty(x => x.Status, SalesInvoiceStatus.Approved));
        var current = await scope.ServiceProvider.GetRequiredService<ISalesInvoiceService>().GetAsync(tenant, created.Id);
        using var approved = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(Convert.ToBase64String(current!.RowVersion), created.Draft), tenantId: tenant);
        Assert.Equal(HttpStatusCode.Conflict, approved.StatusCode);
    }

    [Fact]
    public async Task SalesInvoiceSqlConcurrencyRejectsWriteAfterReadAndPreservesWinner()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync(); var created = await CreateSalesAsync(browser, tenant);
        using var one = f.Scope(); using var two = f.Scope();
        var r1 = one.ServiceProvider.GetRequiredService<ISalesInvoiceRepository>(); var r2 = two.ServiceProvider.GetRequiredService<ISalesInvoiceRepository>();
        var a = (await r1.GetAsync(tenant, created.Id, true, default))!; var b = (await r2.GetAsync(tenant, created.Id, true, default))!;
        var version = a.RowVersion.ToArray();
        a.UpdateDraft(a.ToDraft() with { Notes = "winner" }, f.UserId, DateTimeOffset.UtcNow, new());
        b.UpdateDraft(b.ToDraft() with { Notes = "loser" }, f.UserId, DateTimeOffset.UtcNow, new());
        await r1.SaveAsync(a, version, default);
        await Assert.ThrowsAsync<SalesInvoiceConflictException>(() => r2.SaveAsync(b, version, default));
        using var check = f.Scope(); Assert.Equal("winner", (await check.ServiceProvider.GetRequiredService<ISalesInvoiceService>().GetAsync(tenant, created.Id))!.Notes);
    }

    [Fact]
    public async Task SalesInvoiceSplitReadRetriesWhenAWriterCommitsBetweenHeaderAndChildren()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync(); var created = await CreateSalesAsync(browser, tenant);
        using var scope = f.Scope(); var connection = scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.GetConnectionString();
        var gate = new SalesReadGate();
        await using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer(connection).AddInterceptors(gate).Options);
        var reading = new SalesInvoiceRepository(db).GetAsync(tenant, created.Id, false, default);
        await gate.Arrived.Task.WaitAsync(TimeSpan.FromSeconds(10));
        try
        {
            using var update = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(created.Version, created.Draft with { Lines = [created.Draft.Lines[0] with { UnitPrice = new(200m) }] }), tenantId: tenant);
            Assert.True(update.IsSuccessStatusCode, await update.Content.ReadAsStringAsync());
        }
        finally { gate.Release.TrySetResult(); }
        var result = (await reading)!;
        Assert.Equal(250m, result.NetAmount); Assert.Equal(200m, result.Lines[0].UnitPrice);
        Assert.Equal(result.NetAmount, result.Lines.Sum(x => x.NetAmount)); Assert.Equal(2, result.Revision);
    }

    private sealed class SalesReadGate : DbCommandInterceptor
    {
        private int _blocked;
        public TaskCompletionSource Arrived { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("[SalesInvoiceLines]", StringComparison.Ordinal) && Interlocked.Exchange(ref _blocked, 1) == 0)
            { Arrived.TrySetResult(); await Release.Task.WaitAsync(TimeSpan.FromSeconds(15), cancellationToken); }
            return result;
        }
    }

    [Fact]
    public async Task SalesInvoiceMigrationPrecisionAndCompositeTenantForeignKeysAreRealSqlConstraints()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var otherTenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync(); var created = await CreateSalesAsync(browser, tenant);
        using var scope = f.Scope(); var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(db.Database.HasPendingModelChanges()); Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        var property = db.Model.FindEntityType(typeof(SalesInvoiceLine))!.FindProperty(nameof(SalesInvoiceLine.UnitPrice))!;
        Assert.Equal(22, property.GetPrecision()); Assert.Equal(8, property.GetScale());
        var migrations = db.GetService<IMigrationsAssembly>().Migrations;
        Assert.Contains(migrations.Keys, x => x.EndsWith("AddSalesInvoiceDrafts", StringComparison.Ordinal));
        Assert.Contains(migrations.Keys, x => x.EndsWith("AddSalesInvoiceApprovals", StringComparison.Ordinal));
        Assert.Contains(migrations.Keys, x => x.EndsWith("AddInvoiceNumberSeries", StringComparison.Ordinal));
        Assert.Contains(migrations.Keys, x => x.EndsWith("AddInvoiceNumberReservations", StringComparison.Ordinal));
        Assert.Contains(migrations.Keys, x => x.EndsWith("AddBasicSalesInvoiceFa3Data", StringComparison.Ordinal));
        Assert.Contains(migrations.Keys, x => x.EndsWith("SimplifySalesInvoiceApprovalSnapshots", StringComparison.Ordinal));
        Assert.Contains("AddSalesInvoiceSettlement", migrations.Last().Key);
        await Assert.ThrowsAsync<Microsoft.Data.SqlClient.SqlException>(() => db.Database.ExecuteSqlInterpolatedAsync($"UPDATE SalesInvoiceLines SET TenantId={otherTenant} WHERE SalesInvoiceId={created.Id} AND TenantId={tenant}"));
        Assert.Equal(0, await db.Invoices.CountAsync(x => x.TenantId == tenant));
    }

    [Fact]
    public async Task SalesInvoiceReorderingRemovingAndAddingLinesPreservesSurvivorIds()
    {
        await using var f = await Fixture.CreateAsync(); var tenant = await f.CreateTenantAsync(); var browser = f.Browser();
        using var login = await browser.LoginAsync(); await browser.CsrfAsync(); var input = SalesDraft();
        var created = await CreateSalesAsync(browser, tenant, input with { Lines = [input.Lines[0], input.Lines[0] with { Description = "Druga" }] });
        var first = created.Draft.Lines[0]; var second = created.Draft.Lines[1];
        using var swap = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(created.Version, created.Draft with { Lines = [second, first] }), tenantId: tenant);
        Assert.True(swap.IsSuccessStatusCode, await swap.Content.ReadAsStringAsync());
        var swapped = (await swap.Content.ReadFromJsonAsync<SalesInvoiceDto>())!; Assert.Equal(second.Id, swapped.Draft.Lines[0].Id);
        using var replace = await browser.SendAsync(HttpMethod.Put, $"/api/sales-invoices/{created.Id}", new UpdateSalesInvoiceRequest(swapped.Version, swapped.Draft with { Lines = [second, first with { Id = Guid.Empty }] }), tenantId: tenant);
        Assert.True(replace.IsSuccessStatusCode, await replace.Content.ReadAsStringAsync());
        var replaced = (await replace.Content.ReadFromJsonAsync<SalesInvoiceDto>())!; Assert.Equal(second.Id, replaced.Draft.Lines[0].Id); Assert.NotEqual(first.Id, replaced.Draft.Lines[1].Id);
    }
}
