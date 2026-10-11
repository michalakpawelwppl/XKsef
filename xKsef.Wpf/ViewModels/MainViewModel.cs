using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.Net;
using System.Net.Http;
using xKsef.Contracts.Invoices;
using xKsef.Wpf.Exceptions;
using xKsef.Wpf.Services;

namespace xKsef.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IInvoiceApiClient _invoiceApiClient;
    private DateTimeOffset? _rateLimitUntil;

    public ObservableCollection<InvoiceDto> Invoices { get; } = [];

    [ObservableProperty]
    private DateTime _dateFrom = DateTime.Today.AddDays(-30);

    [ObservableProperty]
    private DateTime _dateTo = DateTime.Today;

    [ObservableProperty]
    private string _status = "Gotowy do pobierania faktur";

    [ObservableProperty]
    private bool _isLoading;

    [ObservableProperty]
    private int selectedTabIndex = 0;

    public ObservableCollection<InvoiceDto> SalesInvoices { get; } = new();

    public ObservableCollection<InvoiceDto> PurchaseInvoices { get; } = new();

    public MainViewModel(IInvoiceApiClient invoiceApiClient)
    {
        _invoiceApiClient = invoiceApiClient;
    }

    [RelayCommand]
    private async Task LoadInvoicesAsync()
    {
        if (_rateLimitUntil is DateTimeOffset until)
        {
            var remaining = until - DateTimeOffset.UtcNow;

            if (remaining > TimeSpan.Zero)
            {
                Status = $"Limit KSeF. Spróbuj ponownie za około " +
                         $"{Math.Ceiling(remaining.TotalMinutes):0} min.";
                return;
            }

            _rateLimitUntil = null;
        }

        if (IsLoading)
            return;


        try
        {
            var direction = SelectedTabIndex switch
            {
                0 => InvoiceDirection.Sales,
                1 => InvoiceDirection.Purchase,
                _ => throw new InvalidOperationException(
                    "Nieznana zakładka faktur.")
            };
            IsLoading = true;
            Status = "Pobieranie faktur...";

            var invoices = await _invoiceApiClient.GetInvoicesAsync(
                DateOnly.FromDateTime(DateFrom),
                DateOnly.FromDateTime(DateTo), direction);

            var targetCollection = direction == InvoiceDirection.Sales
    ? SalesInvoices
    : PurchaseInvoices;

            targetCollection.Clear();

            foreach (var invoice in invoices)
                targetCollection.Add(invoice);

            Status = $"Pobrano {Invoices.Count} faktur.";
        }
        catch (KsefRateLimitException ex)
        {
            if (ex.RetryAfter is TimeSpan delay)
            {
                _rateLimitUntil = DateTimeOffset.UtcNow + delay;

                Status = $"Limit KSeF. Spróbuj ponownie za około " +
                         $"{Math.Ceiling(delay.TotalMinutes):0} min.";
            }
            else
            {
                Status = "Przekroczono limit KSeF. Spróbuj ponownie później.";
            }
        }
        catch (Exception ex)
        {
            Status = $"Błąd: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }


}