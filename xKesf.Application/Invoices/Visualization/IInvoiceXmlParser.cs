namespace xKsef.Application.Invoices.Visualization;

public interface IInvoiceXmlParser
{
    InvoiceVisualizationModel Parse(string xml);
}