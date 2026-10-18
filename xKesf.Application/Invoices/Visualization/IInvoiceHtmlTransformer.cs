namespace xKsef.Application.Invoices.Visualization;

public interface IInvoiceHtmlTransformer
{
    string Transform(string xml);
}