using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using xKsef.Application.Invoices;
using xKsef.Contracts.Invoices;
using xKsef.Domain.Invoices;
using xKsef.Wpf.Exceptions;

namespace xKsef.Wpf.Services;

public class InvoiceApiClient : IInvoiceApiClient
{
    private readonly HttpClient _httpClient;

    public InvoiceApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default)
    {
        var url = $"api/invoices?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&direction={direction}";

        using var response = await _httpClient.GetAsync(url, cancellationToken);

        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            TimeSpan? retryAfter = null;

            var retryHeader = response.Headers.RetryAfter;

            if (retryHeader?.Delta is TimeSpan delta)
            {
                retryAfter = delta;
            }
            else if (retryHeader?.Date is DateTimeOffset date)
            {
                retryAfter = date - DateTimeOffset.UtcNow;

                if (retryAfter < TimeSpan.Zero)
                    retryAfter = TimeSpan.Zero;
            }

            throw new KsefRateLimitException(retryAfter);
        }

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadFromJsonAsync<List<InvoiceDto>>(cancellationToken: cancellationToken) ?? new List<InvoiceDto>();
    }

    public async Task<string> GetInvoiceXmlAsync(
    string ksefNumber,
    CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(ksefNumber))
            throw new ArgumentException(
                "Numer KSeF nie może być pusty.",
                nameof(ksefNumber));

        var url = $"api/invoices/{Uri.EscapeDataString(ksefNumber)}/xml";

        using var response = await _httpClient.GetAsync(
            url,
            cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            TimeSpan? retryAfter = null;

            var retryHeader = response.Headers.RetryAfter;

            if (retryHeader?.Delta is TimeSpan delta)
            {
                retryAfter = delta;
            }
            else if (retryHeader?.Date is DateTimeOffset date)
            {
                retryAfter = date - DateTimeOffset.UtcNow;

                if (retryAfter < TimeSpan.Zero)
                    retryAfter = TimeSpan.Zero;
            }

            throw new KsefRateLimitException(retryAfter);
        }
        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsStringAsync(
            cancellationToken);
    }

    public async Task<byte[]> GetInvoicePdfAsync(
    string ksefNumber,
    CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ksefNumber);

        var url = $"api/invoices/{Uri.EscapeDataString(ksefNumber)}/pdf";

        using var response = await _httpClient.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(cancellationToken);
    }

    public async Task<InvoicePackageReadResult> ImportFromKsefAsync(DateOnly from,DateOnly to, InvoiceDirection direction,CancellationToken cancellationToken = default)
    {
        var url = $"api/invoices/import/ksef?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&direction={direction}";
        using var response = await _httpClient.PostAsync(url, content: null, cancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<InvoicePackageReadResult>(cancellationToken: cancellationToken);

        return result ?? throw new InvalidOperationException("API zwróciło pusty wynik importu faktur.");
    }
}