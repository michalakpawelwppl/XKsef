using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using xKsef.Contracts.Invoices;
using xKsef.Wpf.Exceptions;
using xKsef.Wpf.Http;

namespace xKsef.Wpf.Services;

public class InvoiceApiClient : IInvoiceApiClient
{
    private readonly HttpClient _httpClient;

    public InvoiceApiClient(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<IReadOnlyList<InvoiceDto>> GetInvoicesAsync(ApiRequestContext context, DateOnly from, DateOnly to, InvoiceDirection direction)
    {
        var url = $"api/invoices?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&direction={direction}";

        using var request = CreateRequest(HttpMethod.Get, url, context);
        using var response = await _httpClient.SendAsync(request, context.CancellationToken);

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

        return await response.Content.ReadFromJsonAsync<List<InvoiceDto>>(cancellationToken: context.CancellationToken) ?? new List<InvoiceDto>();
    }

    public async Task<InvoiceQueryResultDto> QueryInvoicesAsync(ApiRequestContext context, DateOnly from, DateOnly to, InvoiceDirection direction, InvoiceDateKind dateKind, string? timeZoneId)
    {
        if (!Enum.IsDefined(dateKind)) throw new ArgumentException("Nieprawidłowy rodzaj daty.", nameof(dateKind));
        var url = FormattableString.Invariant($"api/invoices/query?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&direction={direction}&dateKind={dateKind}");
        if (dateKind == InvoiceDateKind.InvoicingDate)
        {
            if (string.IsNullOrWhiteSpace(timeZoneId)) throw new InvalidOperationException("Nie udało się ustalić strefy czasowej użytkownika.");
            url += "&timeZoneId=" + Uri.EscapeDataString(timeZoneId);
        }
        using var request = CreateRequest(HttpMethod.Get, url, context);
        using var response = await _httpClient.SendAsync(request, context.CancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            TimeSpan? delay = response.Headers.RetryAfter?.Delta;
            if (delay is null && response.Headers.RetryAfter?.Date is DateTimeOffset date)
                delay = date > DateTimeOffset.UtcNow ? date - DateTimeOffset.UtcNow : TimeSpan.Zero;
            throw new KsefRateLimitException(delay);
        }
        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            using var details = await System.Text.Json.JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync(context.CancellationToken), cancellationToken: context.CancellationToken);
            string message = details.RootElement.TryGetProperty("detail", out var detail) && detail.ValueKind == System.Text.Json.JsonValueKind.String
                ? detail.GetString()! : "API odrzuciło filtr faktur lub strefę czasową.";
            throw new HttpRequestException(message, null, response.StatusCode);
        }
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<InvoiceQueryResultDto>(cancellationToken: context.CancellationToken);
        return result is { Items: not null, Summaries: not null } ? result : throw new InvalidOperationException("API zwróciło niekompletny wynik listy i podsumowań faktur.");
    }

    public async Task<byte[]> GetInvoicePdfAsync(ApiRequestContext context, string ksefNumber)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ksefNumber);

        var url = $"api/invoices/{Uri.EscapeDataString(ksefNumber)}/pdf";

        using var request = CreateRequest(HttpMethod.Get, url, context);
        using var response = await _httpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, context.CancellationToken);

        response.EnsureSuccessStatusCode();

        return await response.Content.ReadAsByteArrayAsync(context.CancellationToken);
    }

    public async Task<InvoicePackageReadResult> ImportFromKsefAsync(ApiRequestContext context, DateOnly from, DateOnly to, InvoiceDirection direction)
    {
        var url = $"api/invoices/import/ksef?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&direction={direction}";
        using var request = CreateRequest(HttpMethod.Post, url, context);
        using var response = await _httpClient.SendAsync(request, context.CancellationToken);
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<InvoicePackageReadResult>(cancellationToken: context.CancellationToken);

        return result ?? throw new InvalidOperationException("API zwróciło pusty wynik importu faktur.");
    }

    private static HttpRequestMessage CreateRequest(HttpMethod method, string url, ApiRequestContext context)
    {
        context.CancellationToken.ThrowIfCancellationRequested();

        if (context.Scope != ApiRequestScope.TenantHeader)
            throw new InvalidOperationException("Invoice requests require the TenantHeader scope.");
        if (string.IsNullOrWhiteSpace(context.AccessToken))
            throw new InvalidOperationException("Invoice requests require an access token.");
        if (context.TenantId is not Guid tenantId || tenantId == Guid.Empty)
            throw new InvalidOperationException("Invoice requests require a tenant ID.");

        var request = new HttpRequestMessage(method, url);
        request.SetApiRequestContext(context);
        return request;
    }
}