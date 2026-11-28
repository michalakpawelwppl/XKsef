using System.Text.Json;
using System.Text.Json.Nodes;

namespace xKsef.Application.SalesInvoices.Snapshots;

internal static class SalesInvoiceSnapshotDisclosure
{
    // Projection of the currently supported invoice data.
    // Explicit exclusions: audit/provenance and technical results are never public invoice data.
    private static readonly HashSet<string> InternalNames = new(StringComparer.Ordinal)
    {
        "Id", "SourceContractorId", "Version", "ModelVersion", "NumberSeriesId", "VatMethod", "GlobalDiscountBasis", "PdfLanguage", "PriceBasis",
        "SuggestedValue", "Source", "Reason", "RuleCode", "RuleVersion", "InputFingerprint", "ProposedBy", "ProposedAt", "ConfirmedBy", "ConfirmedAt",
        "RetrievedAt", "IsManualOverride", "OverrideReason", "OriginalSnapshotId", "OriginalRate", "SourceReference", "SelectionRule", "ReferenceDate"
    };

    internal static ApprovedSalesInvoiceDisclosure Create(ApprovedSalesInvoiceDraft input, ApprovedSalesInvoiceCalculations calculations)
    {
        var fields = new List<ApprovedDisclosureField>();
        var ordinals = input.Lines!.ToDictionary(x => x.Id!.Value, x => x.Ordinal);
        void Add(string path, object? value, DisclosureRequirement requirement = DisclosureRequirement.Optional, DisclosureState? state = null)
        {
            var node = value is null ? null : JsonSerializer.SerializeToNode(value, SalesInvoiceSnapshotJson.Options);
            Sanitize(node, ordinals);
            fields.Add(new(path, requirement, state ?? (value is null ? DisclosureState.NotSupplied : DisclosureState.Present),
                node is null ? null : JsonSerializer.SerializeToElement(node).Clone()));
        }
        Add("InvoiceNumber", null, DisclosureRequirement.Required, DisclosureState.FromNumberReservation);
        Add("Kind", input.Kind, DisclosureRequirement.Required); Add("PlannedIssueDate", input.PlannedIssueDate, DisclosureRequirement.Required);
        Add("Currency", input.Currency, DisclosureRequirement.Required); Add("IssuePlace", input.IssuePlace);
        Add("SaleDateMode", input.SaleDateMode, DisclosureRequirement.Conditional); Add("SaleDate", input.SaleDate, DisclosureRequirement.Conditional);
        Add("SalePeriod", input.SalePeriod, DisclosureRequirement.Conditional);
        Add("Seller", input.Seller, DisclosureRequirement.Required); Add("Buyer", input.Buyer, DisclosureRequirement.Required);
        Add("Seller.PlPrefix", input.Seller?.PlPrefix?.Value, DisclosureRequirement.Conditional,
            input.Seller?.PlPrefix?.Value is null or 0 ? DisclosureState.Unresolved : DisclosureState.Present);
        Add("Buyer.PlPrefix", input.Buyer?.PlPrefix?.Value, DisclosureRequirement.Conditional,
            input.Buyer?.PlPrefix?.Value is null or 0 ? DisclosureState.Unresolved : DisclosureState.Present);
        Add("Participants", input.Participants, DisclosureRequirement.Conditional); Add("AuthorizedParty", input.Fa3?.AuthorizedParty, DisclosureRequirement.Conditional);
        Add("WarehouseDocumentNumbers", input.Fa3?.WarehouseDocumentNumbers); Add("Notes", input.Notes); Add("Details", input.Details);
        Add("ExchangeRates", input.ExchangeRates, DisclosureRequirement.Conditional); Add("GlobalDiscount", input.GlobalDiscount, DisclosureRequirement.Conditional);
        foreach (var property in typeof(ApprovedQualifications).GetProperties())
        {
            var decision = input.Fa3?.Qualifications is { } q ? (ApprovedQualificationDecision?)property.GetValue(q) : null;
            Add("Qualifications." + property.Name, decision?.Value, DisclosureRequirement.Conditional,
                decision?.Value is null or 0 ? DisclosureState.Unresolved : DisclosureState.Present);
        }
        foreach (var line in input.Lines!)
        {
            Add($"Lines[{line.Ordinal}].Input", line, DisclosureRequirement.Required);
            Add($"Lines[{line.Ordinal}].Annex15", line.Annex15?.Value, DisclosureRequirement.Optional,
                line.Annex15?.Value is null or 0 ? DisclosureState.Unresolved : DisclosureState.Present);
            var amounts = calculations.Lines!.Single(x => x.Id == line.Id);
            Add($"Lines[{line.Ordinal}].Net", amounts.Net, DisclosureRequirement.Conditional);
            Add($"Lines[{line.Ordinal}].Gross", amounts.Gross, DisclosureRequirement.Conditional);
            var allocated = amounts.VatAmountOrigin == 1;
            Add($"Lines[{line.Ordinal}].Vat", allocated ? null : amounts.Vat, DisclosureRequirement.Conditional, allocated ? DisclosureState.NotDisclosed : DisclosureState.Present);
        }
        Add("Totals", calculations.Totals, DisclosureRequirement.Required);
        Add("TaxSummaries", calculations.TaxSummaries!.Select(x => new { x.TaxCode, x.Net, x.Vat, x.Gross, x.VatPln }).ToArray(), DisclosureRequirement.Required);
        return new(SalesInvoiceSnapshotCapture.ProjectionVersion, fields.AsReadOnly(), Array.AsReadOnly(new[]
        {
            "Input.*.SourceContractorId", "Input.*.qualificationAudit", "Input.VatMethod", "Input.*.PriceBasis", "Input.NumberSeriesId", "Input.Details.AdditionalDescriptions.*.LineId",
            "Calculations.Lines.*.VatAmountOrigin", "Calculations.Lines.*.VatRoundingAdjustment", "Calculations.Lines.*.VatBaseRoundingAdjustment",
            "Calculations.TaxSummaries.*.CalculationDetails", "Metadata", "Input.ExchangeRates.*.provenance"
        }));
    }

    private static void Sanitize(JsonNode? node, IReadOnlyDictionary<Guid, int?> ordinals)
    {
        if (node is JsonObject obj)
        {
            if (obj.TryGetPropertyValue("LineId", out var lineId))
            {
                obj["LineOrdinal"] = lineId is null ? null : JsonValue.Create(ordinals[lineId.GetValue<Guid>()]);
                obj.Remove("LineId");
            }
            foreach (var name in obj.Select(x => x.Key).Where(InternalNames.Contains).ToArray()) obj.Remove(name);
            foreach (var child in obj.ToArray()) Sanitize(child.Value, ordinals);
        }
        else if (node is JsonArray array) foreach (var child in array) Sanitize(child, ordinals);
    }
}
