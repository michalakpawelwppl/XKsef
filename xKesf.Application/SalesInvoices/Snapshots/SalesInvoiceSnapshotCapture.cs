using System.Text.Json;
using System.Text.Json.Serialization;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Application.SalesInvoices.Snapshots;

internal static class SalesInvoiceSnapshotCapture
{
    internal const string ProjectionVersion = "fa3-model-v1";
    internal const string RulesVersion = "issuance-v2";
    private static readonly JsonSerializerOptions CaptureOptions = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions(SalesInvoiceSnapshotJson.Options);
        options.Converters.Add(new DecimalConverter());
        return options;
    }

    // Fail closed on new domain members until the versioned snapshot contract explicitly captures them.
    private static T Copy<T>(object value) => SalesInvoiceSnapshotJson.Read<T>(JsonSerializer.Serialize(value, CaptureOptions));

    internal static ApprovedSalesInvoiceDraft Input(SalesInvoice invoice)
    {
        var input = Copy<ApprovedSalesInvoiceDraft>(invoice.ToDraft());
        return input with
        {
            Lines = Array.AsReadOnly(input.Lines!.Select((x, i) => x with { Ordinal = i + 1 }).ToArray()),
            Participants = Array.AsReadOnly(input.Participants!.Select((x, i) => x with { Ordinal = i + 1 }).ToArray())
        };
    }

    internal static ApprovedSalesInvoiceCalculations Calculations(SalesInvoice invoice, InvoiceCalculationResult? validatedCalculations = null) => new(invoice.CalculationComplete,
        new(invoice.NetAmount, invoice.VatAmount, invoice.GrossAmount, invoice.VatAmountPln, invoice.GlobalDiscountAmount),
        Array.AsReadOnly(invoice.Lines.OrderBy(x => x.Ordinal).Select(x => new ApprovedSalesInvoiceLineResult(x.Id, x.NetAmount, x.VatAmount, x.GrossAmount, x.VatAmountPln,
            x.GlobalDiscountAllocation, x.VatRoundingAdjustment, (int?)x.VatAmountOrigin, x.VatBaseRoundingAdjustment, validatedCalculations?.Lines.Single(line => line.Id == x.Id).LineDiscount)).ToArray()),
        Array.AsReadOnly(invoice.TaxSummaries.OrderBy(x => x.TaxCode, StringComparer.Ordinal).Select(x => new ApprovedSalesInvoiceTaxSummary(x.TaxCode, x.NetAmount, x.VatAmount,
            x.GrossAmount, x.VatAmountPln, x.CalculationDetails is { } d ? new(d.Version, (int)d.Method, (int)d.Basis, d.TaxBase, d.Rate) : null)).ToArray()),
        invoice.SettlementAmounts is null ? null : Copy<ApprovedInvoiceSettlementAmounts>(invoice.SettlementAmounts));

    internal static SalesInvoiceApprovalSnapshot Capture(SalesInvoice invoice, Guid approvalId, string actor, DateTimeOffset now, InvoiceCalculationResult validatedCalculations)
    {
        var input = Input(invoice); var calculations = Calculations(invoice, validatedCalculations);
        var metadata = new ApprovedSalesInvoiceMetadata((int)invoice.Status, Convert.ToBase64String(invoice.RowVersion), invoice.CreatedAt, invoice.UpdatedAt,
            Array.AsReadOnly(invoice.ExchangeRates.OrderBy(x => x.CreatedAt).ThenBy(x => x.Id).Select(x => new ApprovedExchangeRateHistory(Copy<ApprovedExchangeRate>(x.Data), x.IsCurrent, x.CreatedAt, x.CreatedBy)).ToArray()),
            Array.AsReadOnly(Array.Empty<ApprovedSalesInvoiceIssue>()), invoice.InvoiceNumber, invoice.KsefNumber, invoice.FinalNumberReservationId);
        return new(invoice.TenantId, invoice.Id, approvalId, invoice.Revision, actor, now, invoice.ModelVersion, invoice.CalculationVersion,
            ProjectionVersion, RulesVersion, input, calculations, SalesInvoiceSnapshotDisclosure.Create(input, calculations), metadata);
    }

    private sealed class DecimalConverter : JsonConverter<decimal>
    {
        public override decimal Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options) => new SnapshotDecimalConverter().Read(ref reader, typeof(SnapshotDecimal), options).Value;
        public override void Write(Utf8JsonWriter writer, decimal value, JsonSerializerOptions options) => new SnapshotDecimalConverter().Write(writer, new(value), options);
    }
}
