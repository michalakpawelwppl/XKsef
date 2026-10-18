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
    private readonly IWindowNavigationService _navigationService;
    public ObservableCollection<InvoiceDto> Invoices { get; } = [];
    private readonly ITokenStore _tokenStore;

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
    [ObservableProperty]
    private bool isLoggedIn;

    [ObservableProperty]
    private InvoiceDto? selectedSalesInvoice;

    [ObservableProperty]
    private InvoiceDto? selectedPurchaseInvoice;

    public ObservableCollection<InvoiceDto> SalesInvoices { get; } = new();

    public ObservableCollection<InvoiceDto> PurchaseInvoices { get; } = new();

    public MainViewModel(IInvoiceApiClient invoiceApiClient, IWindowNavigationService navigationService, ITokenStore tokenStore)
    {
        _invoiceApiClient = invoiceApiClient;
        _navigationService = navigationService;
        _tokenStore = tokenStore;
        _tokenStore.TokenChanged += OnTokenChanged;
    }

    [RelayCommand]
    private void Login()
    {
        bool? result = _navigationService.ShowLoginWindow();

        if (result == true)
        {
            IsLoggedIn = !string.IsNullOrWhiteSpace(_tokenStore.AccessToken);
        }
    }

    [RelayCommand]
    private void Logout()
    {
        _tokenStore.AccessToken = null;
        IsLoggedIn = false;
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

            Status = $"Pobrano {targetCollection.Count} faktur.";
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

    [RelayCommand]
    private async Task LoadInvoiceXmlAsync()
    {
        InvoiceDto? selectedInvoice = SelectedTabIndex switch
        {
            0 => SelectedSalesInvoice,
            1 => SelectedPurchaseInvoice,
            _ => null
        };

        if (selectedInvoice is null)
        {
            Status = "Najpierw zaznacz fakturę.";
            return;
        }

        try
        {
            Status = "Pobieranie XML faktury...";

            string xml = await _invoiceApiClient.GetInvoiceXmlAsync(
                selectedInvoice.KsefNumber);

            Status = $"Pobrano XML faktury {selectedInvoice.InvoiceNumber}.";
        }
        catch (Exception ex)
        {
            Status = $"Błąd pobierania XML: {ex.Message}";
        }
    }

    partial void OnSelectedSalesInvoiceChanged(InvoiceDto? value)
    {
        if (value is null)
            return;

        LoadInvoiceXmlCommand.Execute(null);
    }

    private void OnTokenChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            IsLoggedIn = !string.IsNullOrWhiteSpace(_tokenStore.AccessToken);
        });
    }

}