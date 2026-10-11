using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using xKsef.Contracts.Invoices;
using xKsef.Wpf.Exceptions;

namespace xKsef.Wpf.Services;

public class InvoiceApiClient : IInvoiceApiClient
{
    private readonly HttpClient _httpClient;

    public InvoiceApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(
        DateOnly from,
        DateOnly to,
        InvoiceDirection direction,
        CancellationToken cancellationToken = default)
    {
        var url = $"api/invoices?from={from:yyyy-MM-dd}" +
                  $"&to={to:yyyy-MM-dd}" +
                  $"&direction={direction}";

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

        return await response.Content.ReadFromJsonAsync<List<InvoiceDto>>(
            cancellationToken: cancellationToken) ?? new List<InvoiceDto>();
    }
}