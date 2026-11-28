using System.Text.Json;
using xKsef.Domain.SalesInvoices;
using xKsef.Domain.SalesInvoices.Calculation;

namespace xKsef.Application.SalesInvoices.Snapshots;

public sealed class SalesInvoiceApprovalSnapshotWriter : ISalesInvoiceApprovalSnapshotWriter
{
    public string Write(SalesInvoice invoice, Guid approvalId, string approvedBy, DateTimeOffset approvedAt, InvoiceCalculationResult? validatedCalculations = null)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        if (invoice.Status != SalesInvoiceStatus.Draft || approvalId == Guid.Empty || string.IsNullOrWhiteSpace(approvedBy))
            throw new SalesInvoiceApprovalSnapshotException("SnapshotIdentityMismatch", "Nieprawidłowe dane zatwierdzenia snapshotu.");
        if (invoice.References.Count != 0) throw new SalesInvoiceApprovalSnapshotException("SnapshotReferencesUnsupported", "Zwykła faktura VAT nie obsługuje jeszcze powiązań korekt i zaliczek.");
        // Production passes the exact result already checked by issuance validation; direct callers are validated here.
        if (validatedCalculations is null)
        {
            var validation = new SalesInvoiceIssuanceValidator(new()).Validate(invoice);
            if (!validation.IsReadyForIssuance) throw new SalesInvoiceValidationException(validation.Errors);
            validatedCalculations = validation.Calculations;
        }
        if (validatedCalculations is not { IsComplete: true }) throw new SalesInvoiceApprovalSnapshotException("SnapshotIncomplete", "Brak zweryfikowanych wyników kalkulatora.");
        var payload = SalesInvoiceSnapshotCapture.Capture(invoice, approvalId, approvedBy, approvedAt, validatedCalculations);
        SalesInvoiceSnapshotValidation.Validate(payload);
        return JsonSerializer.Serialize(payload, SalesInvoiceSnapshotJson.Options);
    }

}

public static class ApprovedSalesInvoiceComparison
{
    // Compare captured inputs and stored results. Never call a calculator or an API mapper here.
    public static bool Matches(SalesInvoice invoice, SalesInvoiceApprovalSnapshot document)
    {
        if (document.TenantId != invoice.TenantId || document.InvoiceId != invoice.Id) return false;
        // The aggregate stores the discount input, not its exact calculated amount. Its input is compared below;
        // the applied amount is captured from validated inputs and must never be recomputed during numbering.
        var persisted = document.Calculations is { Lines: not null } c
            ? c with { Lines = Array.AsReadOnly(c.Lines.Select(x => x with { LineDiscount = null }).ToArray()) } : document.Calculations;
        var currentInput = SalesInvoiceSnapshotCapture.Input(invoice);
        // Historical JSON has no factor collection. Accept its absence only when the persisted invoice has none.
        // Keep the reader's null (unknown/not captured) intact; never hide an actual factor account change.
        if (document.Input?.Details?.FactorBankAccounts is null && currentInput.Details?.FactorBankAccounts is { Count: 0 })
            currentInput = currentInput with { Details = currentInput.Details with { FactorBankAccounts = null } };
        if (document.Input?.Details?.PartialPayments is null && currentInput.Details?.PartialPayments is { Count: 0 })
            currentInput = currentInput with { Details = currentInput.Details with { PartialPayments = null } };
        return document.CalculatorVersion == invoice.CalculationVersion
            && SalesInvoiceSnapshotJson.Same(document.Input, currentInput)
            && SalesInvoiceSnapshotJson.Same(persisted, SalesInvoiceSnapshotCapture.Calculations(invoice));
    }
}
