using System.Globalization;
using System.Text.Json;
using xKsef.Application.Invoices;
using xKsef.Contracts.Invoices;
using xKsef.Infrastructure.Invoices;

namespace xKsef.Tests.Infrastructure;

public sealed class InvoiceQueryTests
{
    [Fact]
    public void DefaultFilterUsesInclusiveIssueDatesWithoutATimeZone()
    {
        var from = new DateOnly(2026, 11, 1);
        var to = new DateOnly(2026, 11, 30);
        var filter = InvoiceQueryFilter.Create(from, to, InvoiceDirection.Sales);
        Assert.Equal(InvoiceDateKind.IssueDate, filter.DateKind);
        Assert.Equal(from, filter.From);
        Assert.Equal(to, filter.To);
        Assert.Equal(InvoiceDirection.Sales, filter.Direction);
        Assert.Null(filter.FromUtc);
        Assert.Null(filter.ToUtcExclusive);
    }

    [Theory]
    [InlineData("2026-03-29", "Europe/Warsaw", "2026-03-28T23:00:00Z", "2026-03-29T22:00:00Z", 23)]
    [InlineData("2026-10-25", "Europe/Warsaw", "2026-10-24T22:00:00Z", "2026-10-25T23:00:00Z", 25)]
    [InlineData("2026-07-15", "Europe/Warsaw", "2026-07-14T22:00:00Z", "2026-07-15T22:00:00Z", 24)]
    [InlineData("2026-01-15", "Europe/Warsaw", "2026-01-14T23:00:00Z", "2026-01-15T23:00:00Z", 24)]
    [InlineData("2026-07-15", "Asia/Kathmandu", "2026-07-14T18:15:00Z", "2026-07-15T18:15:00Z", 24)]
    [InlineData("2026-07-15", "UTC", "2026-07-15T00:00:00Z", "2026-07-16T00:00:00Z", 24)]
    public void LocalDaysProduceUtcHalfOpenRanges(string date, string zone, string start, string end, int hours)
    {
        var day = DateOnly.Parse(date, CultureInfo.InvariantCulture);
        var filter = InvoiceQueryFilter.Create(day, day, InvoiceDirection.Purchase, InvoiceDateKind.InvoicingDate, zone);
        Assert.Equal(DateTimeOffset.Parse(start, CultureInfo.InvariantCulture), filter.FromUtc);
        Assert.Equal(DateTimeOffset.Parse(end, CultureInfo.InvariantCulture), filter.ToUtcExclusive);
        Assert.Equal(TimeSpan.Zero, filter.FromUtc!.Value.Offset);
        Assert.Equal(TimeSpan.Zero, filter.ToUtcExclusive!.Value.Offset);
        Assert.Equal(hours, (filter.ToUtcExclusive.Value - filter.FromUtc.Value).TotalHours);
    }

    [Fact]
    public void BoundariesUseTheirOwnOffsetsAcrossASeasonChange()
    {
        var filter = InvoiceQueryFilter.Create(new DateOnly(2026, 3, 28), new DateOnly(2026, 3, 30), InvoiceDirection.Sales, InvoiceDateKind.InvoicingDate, "Europe/Warsaw");
        Assert.Equal(DateTimeOffset.Parse("2026-03-27T23:00:00Z", CultureInfo.InvariantCulture), filter.FromUtc);
        Assert.Equal(DateTimeOffset.Parse("2026-03-30T22:00:00Z", CultureInfo.InvariantCulture), filter.ToUtcExclusive);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("Invalid/Zone")]
    public void InvoicingDateRequiresAResolvableZone(string? zone)
    {
        Assert.Throws<ArgumentException>(() => InvoiceQueryFilter.Create(new DateOnly(2026, 1, 1), new DateOnly(2026, 1, 1), InvoiceDirection.Sales, InvoiceDateKind.InvoicingDate, zone));
    }

    [Theory]
    [InlineData("2026-03-08")]
    [InlineData("2026-11-01")]
    public void NonexistentOrAmbiguousMidnightIsRejected(string date)
    {
        var day = DateOnly.Parse(date, CultureInfo.InvariantCulture);
        Assert.Throws<ArgumentException>(() => InvoiceQueryFilter.Create(day, day, InvoiceDirection.Sales, InvoiceDateKind.InvoicingDate, "America/Havana"));
    }

    [Fact]
    public void InvalidDatesEnumsAndUnrepresentableBoundariesAreRejected()
    {
        var day = new DateOnly(2026, 1, 1);
        Assert.Throws<ArgumentException>(() => InvoiceQueryFilter.Create(day.AddDays(1), day, InvoiceDirection.Sales));
        Assert.Throws<ArgumentException>(() => InvoiceQueryFilter.Create(day, day, (InvoiceDirection)0));
        Assert.Throws<ArgumentException>(() => InvoiceQueryFilter.Create(day, day, InvoiceDirection.Sales, (InvoiceDateKind)999));
        Assert.Throws<ArgumentException>(() => InvoiceQueryFilter.Create(day, DateOnly.MaxValue, InvoiceDirection.Sales, InvoiceDateKind.InvoicingDate, "UTC"));
        Assert.ThrowsAny<ArgumentException>(() => InvoiceQueryFilter.Create(DateOnly.MinValue, DateOnly.MinValue, InvoiceDirection.Sales, InvoiceDateKind.InvoicingDate, "Asia/Kathmandu"));
    }

    [Fact]
    public void SummariesSeparateCurrenciesIncludeCorrectionsAndIgnoreVatAmountPln()
    {
        InvoiceDto[] items =
        [
            new() { Currency = "PLN", NetAmount = 100m, VatAmount = 23m, GrossAmount = 123m, VatAmountPln = 23m },
            new() { Currency = "PLN", NetAmount = -40m, VatAmount = -9.2m, GrossAmount = -49.2m, VatAmountPln = -9.2m },
            new() { Currency = "EUR", NetAmount = 100m, VatAmount = 23m, GrossAmount = 123m, VatAmountPln = 999m },
            new() { Currency = "USD", NetAmount = -10m, VatAmount = -2m, GrossAmount = -12m }
        ];
        var summaries = InvoiceQueryService.Summarize(items);
        Assert.Equal(new[] { "EUR", "PLN", "USD" }, summaries.Select(summary => summary.Currency));
        Assert.Equal(new InvoiceCurrencySummaryDto("EUR", 1, 100m, 23m, 123m, 0, 0), summaries[0]);
        Assert.Equal(new InvoiceCurrencySummaryDto("PLN", 2, 60m, 13.8m, 73.8m, 0, 0), summaries[1]);
        Assert.Equal(new InvoiceCurrencySummaryDto("USD", 1, -10m, -2m, -12m, 0, 0), summaries[2]);
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void APartialSumIsNullForEachIncompleteAmountIndependently(bool missingNet, bool missingVat)
    {
        InvoiceDto[] items =
        [
            new() { Currency = "EUR", NetAmount = 100m, VatAmount = 23m, GrossAmount = 123m },
            new() { Currency = "EUR", NetAmount = missingNet ? null : 0m, VatAmount = missingVat ? null : 0m, GrossAmount = -23m }
        ];
        var summary = Assert.Single(InvoiceQueryService.Summarize(items));
        Assert.Equal(missingNet ? (decimal?)null : 100m, summary.NetTotal);
        Assert.Equal(missingVat ? (decimal?)null : 23m, summary.VatTotal);
        Assert.Equal(missingNet ? 1 : 0, summary.MissingNetCount);
        Assert.Equal(missingVat ? 1 : 0, summary.MissingVatCount);
        Assert.Equal(100m, summary.GrossTotal);
        Assert.Equal(2, summary.InvoiceCount);
    }

    [Fact]
    public void ZeroAllMissingAndNoInvoicesHaveDifferentRepresentations()
    {
        var zero = Assert.Single(InvoiceQueryService.Summarize([new InvoiceDto { Currency = "PLN", NetAmount = 0m, VatAmount = 0m }]));
        Assert.Equal(0m, zero.NetTotal);
        Assert.Equal(0m, zero.VatTotal);
        Assert.Equal(0, zero.MissingNetCount);
        var absent = Assert.Single(InvoiceQueryService.Summarize([new InvoiceDto { Currency = "PLN" }, new InvoiceDto { Currency = "PLN" }]));
        Assert.Null(absent.NetTotal);
        Assert.Null(absent.VatTotal);
        Assert.Equal(2, absent.MissingNetCount);
        Assert.Equal(2, absent.MissingVatCount);
        Assert.Empty(InvoiceQueryService.Summarize([]));
        var json = JsonSerializer.SerializeToElement(absent, JsonSerializerOptions.Web);
        Assert.Equal(JsonValueKind.Null, json.GetProperty("netTotal").ValueKind);
        Assert.Equal(0m, json.GetProperty("grossTotal").GetDecimal());
    }
}
