using xKsef.Application.Invoices;
using xKsef.Domain.Invoices;

namespace xKsef.Infrastructure.Invoices;

internal static class InvoicePackageValidator
{
    public static void ValidateFileExists(
        string directoryPath,
        ExportedInvoiceMetadata invoice)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var filePath = Path.Combine(
            directoryPath,
            invoice.FileName);

        if (!File.Exists(filePath))
        {
            throw new FileNotFoundException(
                $"Nie znaleziono pliku XML dla faktury {invoice.KsefNumber}.",
                filePath);
        }
    }

    public static void ValidateAllFilesExist(
    string directoryPath,
    IReadOnlyList<ExportedInvoiceMetadata> invoices)
    {
        ArgumentNullException.ThrowIfNull(invoices);
        foreach (var invoice in invoices)
        {
            ValidateFileName(invoice);
            ValidateFileExists(directoryPath, invoice);
        }
    }

    private static void ValidateFileName(
    ExportedInvoiceMetadata invoice)
    {
        if (string.IsNullOrWhiteSpace(invoice.KsefNumber))
        {
            throw new InvalidOperationException(
                "Brak numeru KSeF faktury.");
        }

        if (string.IsNullOrWhiteSpace(invoice.FileName))
        {
            throw new InvalidOperationException(
                "Brak nazwy pliku faktury.");
        }

        if (invoice.FileName != $"{invoice.KsefNumber}.xml")
        {
            throw new InvalidOperationException(
                "Nazwa pliku nie odpowiada numerowi KSeF.");
        }

        if (invoice.FileName != Path.GetFileName(invoice.FileName)
            || invoice.FileName.Contains('\\')
            || invoice.FileName.Contains('/')
            || invoice.FileName.Contains(':'))
        {
            throw new InvalidOperationException(
                "Niedozwolona ścieżka pliku faktury.");
        }
    }

    public static async Task<ValidatedInvoice> ValidateInvoiceAsync(
     string directoryPath,
     ExportedInvoiceMetadata invoice,
     CancellationToken cancellationToken = default)
    {
        ValidateFileExists(directoryPath, invoice);

        var filePath = Path.Combine(
            directoryPath,
            invoice.FileName);

        byte[] xmlBytes = await File.ReadAllBytesAsync(
            filePath,
            cancellationToken);

        if (!InvoiceHashValidator.IsValid(
            xmlBytes,
            invoice.InvoiceHash))
        {
            throw new InvalidOperationException(
                $"Niezgodny skrót SHA-256 faktury {invoice.KsefNumber}.");
        }

        return new ValidatedInvoice(
            invoice.KsefNumber,
            xmlBytes,
            invoice.VatAmountPln,
            invoice.InvoicingDate);
    }

    public static async Task<IReadOnlyList<ValidatedInvoice>>
    ValidateAllInvoicesAsync(
        string directoryPath,
        IReadOnlyList<ExportedInvoiceMetadata> invoices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(invoices);

        var validatedInvoices = new List<ValidatedInvoice>();

        foreach (var invoice in invoices)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var validatedInvoice = await ValidateInvoiceAsync(
                directoryPath,
                invoice,
                cancellationToken);

            validatedInvoices.Add(validatedInvoice);
        }

        return validatedInvoices;
    }
}