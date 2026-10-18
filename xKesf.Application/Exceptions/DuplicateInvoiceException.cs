namespace xKsef.Application.Exceptions;

public sealed class DuplicateInvoiceException : Exception
{
    public string KsefNumber { get; }

    public DuplicateInvoiceException(
        string ksefNumber,
        Exception? innerException = null)
        : base(
            $"Faktura o numerze KSeF '{ksefNumber}' już istnieje.",
            innerException)
    {
        KsefNumber = ksefNumber;
    }
}