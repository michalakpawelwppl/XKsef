namespace xKsef.Application.Invoices;

public sealed class ValidatedInvoice
{
    private readonly byte[] _xmlBytes;

    public string KsefNumber { get; }

    public decimal? VatAmountPln { get; }

    public DateTimeOffset? InvoicingDate { get; }

    public ValidatedInvoice(string ksefNumber, byte[] xmlBytes) : this(ksefNumber, xmlBytes, null, null)
    {
    }

    public ValidatedInvoice(string ksefNumber, byte[] xmlBytes, decimal? vatAmountPln, DateTimeOffset? invoicingDate)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ksefNumber);
        ArgumentNullException.ThrowIfNull(xmlBytes);

        KsefNumber = ksefNumber;
        VatAmountPln = vatAmountPln;
        InvoicingDate = invoicingDate;
        _xmlBytes = (byte[])xmlBytes.Clone();
    }

    public byte[] GetXmlBytes()
    {
        return (byte[])_xmlBytes.Clone();
    }
}