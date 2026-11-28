using System.Globalization;
using xKsef.Domain.InvoiceNumberSeries;
using xKsef.Domain.SalesInvoices;
using NumberSeries = xKsef.Domain.InvoiceNumberSeries.InvoiceNumberSeries;

namespace xKsef.Tests.Domain;

public sealed class InvoiceNumberSeriesTests
{
    private static NumberSeriesConfiguration Configuration(string prefix = "FV", string format = "{prefix}/{number}/{year}", int digits = 6, NumberSeriesResetMode reset = NumberSeriesResetMode.Yearly)
        => new(prefix, prefix, format, digits, reset);

    [Theory]
    [InlineData("{prefix}/{number}/{year}", "FV", 6, "FV/000001/2026")]
    [InlineData("{prefix}/{year}/{number}", "FV", 6, "FV/2026/000001")]
    [InlineData("{number}/{prefix}", "FV", 6, "000001/FV")]
    [InlineData("{prefix}/{number}/{year}", "FK", 4, "FK/0001/2026")]
    public void ExamplesRenderDeterministicallyWithoutAllocatingNumbers(string format, string prefix, int digits, string expected)
    {
        var configuration = Configuration(prefix, format, digits, format.Contains("{year}") ? NumberSeriesResetMode.Yearly : NumberSeriesResetMode.None);
        foreach (var culture in new[] { "pl-PL", "en-US", "ar-SA" })
        {
            var before = CultureInfo.CurrentCulture;
            try { CultureInfo.CurrentCulture = new(culture); Assert.Equal(expected, InvoiceNumberFormat.Render(configuration, 1, new(2026, 12, 31))); }
            finally { CultureInfo.CurrentCulture = before; }
        }
        Assert.Equal(1, configuration.StartingNumber);
    }

    [Fact]
    public void YearComesOnlyFromIssueDateAndPaddingNeverTruncates()
    {
        Assert.Equal("FV/1234567/1999", InvoiceNumberFormat.Render(Configuration(), 1234567, new(1999, 12, 31)));
        Assert.Equal("FV/000001/0001", InvoiceNumberFormat.Render(Configuration(), 1, DateOnly.MinValue));
        Assert.Equal("FV/9223372036854775807/9999", InvoiceNumberFormat.Render(Configuration(), long.MaxValue, DateOnly.MaxValue));
    }

    [Theory]
    [InlineData("{prefix}/{year}")]
    [InlineData("{number}/{number}/{year}")]
    [InlineData("{prefix}/{number}/{month}/{year}")]
    [InlineData("{prefix}/{number}/{year}/{year}")]
    [InlineData("{prefix}/{number}")]
    [InlineData("{number}/{{year}}")]
    [InlineData("{number}/\n{year}")]
    public void InvalidAndAmbiguousTemplatesAreRejected(string format)
        => Assert.Throws<InvoiceNumberSeriesValidationException>(() => InvoiceNumberFormat.Validate(Configuration(format: format)));

    [Fact]
    public void ConfigurationValidationCollectsErrorsWithoutChangingTheSeries()
    {
        var now = DateTimeOffset.UtcNow; var series = NumberSeries.Create(Guid.NewGuid(), Configuration(), "user", now);
        var original = series.Configuration;
        var invalid = Configuration() with { Name = "", Prefix = "{number}", MinimumDigits = 0, StartingNumber = 0, ResetMode = (NumberSeriesResetMode)99 };
        var error = Assert.Throws<InvoiceNumberSeriesValidationException>(() => series.UpdateConfiguration(invalid, "user", now));
        Assert.True(error.Issues.Count >= 5); Assert.Equal(original, series.Configuration); Assert.Equal(1, series.Revision);
    }

    [Fact]
    public void YearlyResetNeedsYearButContinuousNumberingDoesNot()
    {
        InvoiceNumberFormat.Validate(Configuration(format: "{number}/{prefix}", reset: NumberSeriesResetMode.None));
        Assert.Throws<InvoiceNumberSeriesValidationException>(() => InvoiceNumberFormat.Validate(Configuration(format: "{number}/{prefix}")));
    }

    [Theory]
    [InlineData("FV", "{prefix}/{number}/{year}", 6, "FK", "{prefix}/{number}/{year}", 6, false)]
    [InlineData("FV", "{prefix}/{number}/{year}", 1, "FV", "{prefix}/{number}/{year}", 6, true)]
    [InlineData("FV1", "{prefix}{number}/{year}", 1, "FV", "{prefix}{number}/{year}", 1, true)]
    [InlineData("fv", "{prefix}/{number}/{year}", 6, "FV", "{prefix}/{number}/{year}", 6, true)]
    [InlineData("UNUSED", "FV/{number}/{year}", 6, "FV", "{prefix}/{number}/{year}", 6, true)]
    [InlineData("FV", "{prefix}/{number}/{year}", 6, "FV", "{prefix}/{year}/{number}", 6, false)]
    [InlineData("FV", "{prefix}/{number}/{year}", 4, "FV", "{prefix}/{year}/{number}", 4, true)]
    public void CollisionDetectionComparesFullOutputLanguages(string prefixA, string formatA, int digitsA, string prefixB, string formatB, int digitsB, bool overlaps)
    {
        var a = Configuration(prefixA, formatA, digitsA); var b = Configuration(prefixB, formatB, digitsB);
        Assert.Equal(overlaps, InvoiceNumberFormat.FindCollision(a, b) is not null);
        Assert.Equal(overlaps, InvoiceNumberFormat.FindCollision(b, a) is not null);
    }

    [Theory]
    [InlineData("9223372036854775807", true)]
    [InlineData("9223372036854775808", false)]
    [InlineData("0000000000000000000", false)]
    public void CollisionDetectionRespectsActualNumericBounds(string literal, bool overlaps)
    {
        var left = Configuration();
        var right = Configuration(format: "{prefix}/" + literal + "/{number}", digits: 4, reset: NumberSeriesResetMode.None);
        Assert.Equal(overlaps, InvoiceNumberFormat.FindCollision(left, right) is not null);
    }

    [Fact]
    public void HistoryRetainsEveryConfigurationAndDoesNotIntroduceACounter()
    {
        var now = DateTimeOffset.UtcNow; var series = NumberSeries.Create(Guid.NewGuid(), Configuration(), "creator", now);
        series.UpdateConfiguration(Configuration(format: "{prefix}/{year}/{number}") with { StartingNumber = 10 }, "editor", now.AddMinutes(1));
        series.RecordDefaultChange(true, SalesInvoiceKind.Vat, "editor", now.AddMinutes(2));
        series.RecordDefaultChange(true, SalesInvoiceKind.Advance, "editor", now.AddMinutes(3));
        series.SetActive(false, "editor", now.AddMinutes(4)); series.SetActive(true, "editor", now.AddMinutes(5));
        Assert.Equal(6, series.Revision); Assert.Equal(6, series.History.Count); Assert.Equal(1, series.History[0].Configuration.StartingNumber);
        Assert.Equal("{prefix}/{number}/{year}", series.History[0].Configuration.Format);
        Assert.Equal("creator", series.CreatedBy); Assert.Equal("editor", series.UpdatedBy); Assert.Equal(now.AddMinutes(5), series.UpdatedAt);
        Assert.Equal(NumberSeriesChangeKind.DefaultAssigned, series.History[2].Change); Assert.True(series.IsActive);
        series.SetActive(false, "editor", now);
        Assert.Throws<InvoiceNumberSeriesValidationException>(() => series.RecordDefaultChange(true, SalesInvoiceKind.Correction, "editor", now));
    }
}
