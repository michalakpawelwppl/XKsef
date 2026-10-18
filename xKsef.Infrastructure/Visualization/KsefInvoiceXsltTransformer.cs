using System.Xml;
using System.Xml.Xsl;
using xKsef.Application.Invoices.Visualization;

namespace xKsef.Infrastructure.Invoices.Visualization;

public sealed class KsefInvoiceXsltTransformer
    : IInvoiceHtmlTransformer
{
    private readonly XslCompiledTransform _transform;

    public KsefInvoiceXsltTransformer()
    {
        _transform = new XslCompiledTransform();

        string xsltDirectory = Path.Combine(
            AppContext.BaseDirectory,
            "Visualization");

        string xsltPath = Path.Combine(
            xsltDirectory,
            "styl.xsl");

        var resolver = new LocalXsltResolver(xsltDirectory);

        _transform.Load(
            xsltPath,
            XsltSettings.Default,
            resolver);
    }

    public string Transform(string xml)
    {
        using var stringReader = new StringReader(xml);

        using var xmlReader = XmlReader.Create(
            stringReader,
            new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null
            });

        using var stringWriter = new StringWriter();

        using var xmlWriter = XmlWriter.Create(
            stringWriter,
            _transform.OutputSettings.Clone());

        _transform.Transform(
            xmlReader,
            null,
            xmlWriter);

        xmlWriter.Flush();

        return stringWriter.ToString();
    }
}