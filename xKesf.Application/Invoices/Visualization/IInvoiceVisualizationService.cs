namespace xKsef.Application.Invoices.Visualization;

public interface IInvoiceVisualizationService
{
    Task<byte[]> GeneratePdfAsync(
        string xml,
        CancellationToken cancellationToken = default);
    Task<byte[]> GenerateOfficialPdfAsync(
    string xml,
    string ksefNumber,
    CancellationToken cancellationToken = default);
}