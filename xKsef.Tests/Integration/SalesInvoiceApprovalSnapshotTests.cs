using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using xKsef.Application.SalesInvoices.Snapshots;
using xKsef.Domain.InvoiceNumberSeries;
using xKsef.Domain.SalesInvoices;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Integration;

public sealed partial class BrowserAuthenticationTests
{
    [Fact]
    public async Task ApprovalSnapshotReaderAndNumberingKeepCurrentSnapshotUnchanged()
    {
        await using var f = await Fixture.CreateAsync();
        var (tenant, browser, series) = await NumberingScenarioAsync(f);
        var invoice = await NumberingInvoiceAsync(browser, tenant, series.Id);
        string before;
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var approval = await db.Set<SalesInvoiceApproval>().AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.SalesInvoiceId == invoice.Id);
            before = JsonSerializer.Serialize(approval);
            var document = scope.ServiceProvider.GetRequiredService<ISalesInvoiceApprovalSnapshotReader>().Read(approval);
            Assert.Equal(tenant, document.TenantId);
            Assert.Equal(series.Id, document.Input!.NumberSeriesId); Assert.NotNull(document.Disclosure);
        }
        var reservation = await ReserveNumberAsync(f, tenant, NumberCommand(invoice));
        var replay = await ReserveNumberAsync(f, tenant, NumberCommand(invoice, reservation.OperationId));
        Assert.Equal(reservation.Id, replay.Id);
        using (var scope = f.Scope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var approval = await db.Set<SalesInvoiceApproval>().AsNoTracking().SingleAsync(x => x.TenantId == tenant && x.SalesInvoiceId == invoice.Id);
            Assert.Equal(before, JsonSerializer.Serialize(approval));
        }
    }

}
