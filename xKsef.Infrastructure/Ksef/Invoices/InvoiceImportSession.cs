namespace xKsef.Infrastructure.Ksef.Invoices;

internal sealed class InvoiceImportSession
{
    public required string ReferenceNumber { get; init; }

    public required byte[] CipherKey { get; init; }

    public required byte[] CipherIv { get; init; }
}