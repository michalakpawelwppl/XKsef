namespace xKsef.Application.Invoices;

public sealed class ValidatedInvoice
{
    private readonly byte[] _xmlBytes;

    public string KsefNumber { get; }

    public ValidatedInvoice(string ksefNumber, byte[] xmlBytes)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ksefNumber);
        ArgumentNullException.ThrowIfNull(xmlBytes);

        KsefNumber = ksefNumber;
        _xmlBytes = (byte[])xmlBytes.Clone();
    }

    public byte[] GetXmlBytes()
    {
        return (byte[])_xmlBytes.Clone();
    }
}