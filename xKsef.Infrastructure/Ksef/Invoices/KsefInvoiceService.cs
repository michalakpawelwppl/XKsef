using KSeF.Client.Core.Exceptions;
using KSeF.Client.Core.Interfaces.Clients;
using KSeF.Client.Core.Models.Invoices;
using Microsoft.Extensions.Options;
using xKsef.Application.Exceptions;
using xKsef.Application.Invoices;
using xKsef.Domain.Invoices;
using xKsef.Infrastructure.Ksef.Authentication;

namespace xKsef.Infrastructure.Ksef.Invoices;

public sealed class KsefInvoiceService : IInvoiceService
{
    private readonly IKSeFClient _ksefClient;
    private readonly KsefTokenProvider _tokenProvider;
    private readonly KsefRateLimitState _rateLimitState;

    public KsefInvoiceService(
        IKSeFClient ksefClient,
        KsefTokenProvider tokenProvider, KsefRateLimitState rateLimitState)
    {
        _ksefClient = ksefClient;
        _tokenProvider = tokenProvider;
        _rateLimitState = rateLimitState;
    }

    public async Task<int> GetInvoiceCountAsync(DateOnly from, DateOnly to, CancellationToken cancellationToken = default)
    {
        if (from > to)
            throw new ArgumentException(
                "Data początkowa nie może być późniejsza od końcowej.");

        var accessToken = await _tokenProvider.GetAccessTokenAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException("Nie udało się uzyskać tokena KSeF.");

        var filters = new InvoiceQueryFilters
        {
            SubjectType = InvoiceSubjectType.Subject1,
            DateRange = new DateRange
            {
                From = from.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc),
                To = to.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc),
                DateType = DateType.Issue
            }
        };

        const int pageSize = 50;

        int pageOffset = 0;
        int totalCount = 0;

        while (true)
        {
            var response = await QueryInvoicePageAsync(
                filters,
                accessToken,
                pageOffset,
                pageSize,
                cancellationToken);
            if (response.IsTruncated)
            {
                throw new InvalidOperationException("Wynik zapytania KSeF został ograniczony do 10 000 faktur. " + "Wybierz krótszy zakres dat.");
            }
            // Dotychczasowa obsługa odpowiedzi
            int count = response.Invoices?.Count ?? 0;

            totalCount += count;

            if (!response.HasMore)
                return totalCount;

            pageOffset++;
        }
    }

    public async Task<IReadOnlyList<InvoiceSummary>> GetInvoicesMetadataAsync(DateOnly from, DateOnly to, InvoiceDirection direction, CancellationToken cancellationToken = default)
    {
        if (from > to)
            throw new ArgumentException(
                "Data początkowa nie może być późniejsza od końcowej.");
        var remainingDelay = _rateLimitState.GetRemainingDelay();

        if (remainingDelay.HasValue)
        {
            throw new ExternalServiceRateLimitException(
                "Limit żądań KSeF nadal obowiązuje.",
                retryAfter: remainingDelay.Value);
        }

        var accessToken = await _tokenProvider
            .GetAccessTokenAsync(cancellationToken);

        if (string.IsNullOrWhiteSpace(accessToken))
            throw new InvalidOperationException(
                "Nie udało się uzyskać tokena KSeF.");

        var filters = new InvoiceQueryFilters
        {
            SubjectType = direction switch
            {
                InvoiceDirection.Sales => InvoiceSubjectType.Subject1,
                InvoiceDirection.Purchase => InvoiceSubjectType.Subject2,
                _ => throw new ArgumentOutOfRangeException(nameof(direction))
            },
            DateRange = new DateRange
            {
                From = from.ToDateTime(
                    TimeOnly.MinValue, DateTimeKind.Utc),
                To = to.ToDateTime(
                    TimeOnly.MaxValue, DateTimeKind.Utc),
                DateType = DateType.Issue
            }
        };

        const int pageSize = 50;
        int pageOffset = 0;

        var invoices = new List<InvoiceSummary>();

        while (true)
        {
            var response = await QueryInvoicePageAsync(
                filters,
                accessToken,
                pageOffset,
                pageSize,
                cancellationToken);

            if (response.IsTruncated)
            {
                throw new InvalidOperationException(
                    "Wynik zapytania KSeF został ograniczony do 10 000 faktur. " +
                    "Wybierz krótszy zakres dat.");
            }

            if (response.Invoices is not null)
                invoices.AddRange(response.Invoices);

            if (!response.HasMore)
                break;

            pageOffset++;
        }

        return invoices;
    }

    public async Task<IReadOnlyList<Invoice>> GetInvoicesAsync(
    DateOnly from,
    DateOnly to,
    InvoiceDirection direction,
    CancellationToken cancellationToken = default)
    {
        if (from > to)
            throw new ArgumentException("Niepoprawny zakres dat.");

        var summaries = await GetInvoicesMetadataAsync(
            from,
            to,
            direction,
            cancellationToken);

        return summaries.Select(summary => new Invoice
        {
            KsefNumber = summary.KsefNumber,
            InvoiceNumber = summary.InvoiceNumber,
            IssueDate = DateOnly.FromDateTime(summary.IssueDate.Date),
            GrossAmount = summary.GrossAmount,
            Currency = summary.Currency,
            SellerName = summary.Seller?.Name ?? string.Empty,
            SellerNip = summary.Seller?.Nip ?? string.Empty
        }).ToList();
    }

    private async Task<PagedInvoiceResponse> QueryInvoicePageAsync(
    InvoiceQueryFilters filters,
    string accessToken,
    int pageOffset,
    int pageSize,
    CancellationToken cancellationToken)
    {
        var remaining = _rateLimitState.GetRemainingDelay();

        if (remaining.HasValue)
        {
            throw new ExternalServiceRateLimitException(
                "Limit żądań KSeF nadal obowiązuje.",
                retryAfter: remaining.Value);
        }

        try
        {
            return await _ksefClient.QueryInvoiceMetadataAsync(
                filters,
                accessToken,
                pageOffset: pageOffset,
                pageSize: pageSize,
                cancellationToken: cancellationToken);
        }
        catch (KsefRateLimitException ex)
        {
            TimeSpan? retryAfter = null;

            if (ex.RetryAfterSeconds.HasValue)
            {
                retryAfter = TimeSpan.FromSeconds(
                    Math.Max(0, ex.RetryAfterSeconds.Value));
            }
            else if (ex.RetryAfterDate.HasValue)
            {
                retryAfter = ex.RetryAfterDate.Value - DateTimeOffset.UtcNow;

                if (retryAfter < TimeSpan.Zero)
                    retryAfter = TimeSpan.Zero;
            }

            if (retryAfter.HasValue)
                _rateLimitState.BlockFor(retryAfter.Value);

            throw new ExternalServiceRateLimitException(
                "Przekroczono limit żądań KSeF.",
                retryAfter: retryAfter,
                innerException: ex);
        }
    }

}