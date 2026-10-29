using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using xKsef.Contracts.Invoices;
using xKsef.Domain.Invoices;
using xKsef.Wpf.Exceptions;

namespace xKsef.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly IInvoiceApiClient _invoiceApiClient;
    private DateTimeOffset? _rateLimitUntil;
    private readonly IWindowNavigationService _navigationService;
    public ObservableCollection<InvoiceDto> Invoices { get; } = [];
    private readonly ITokenStore _tokenStore;

    private CancellationTokenSource? _invoicePdfCancellationTokenSource;
    private string? _currentTemporaryPdfPath;
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
    [ObservableProperty]
    private string? selectedInvoicePdfPath;
    public ObservableCollection<InvoiceDto> SalesInvoices { get; } = new();

    public ObservableCollection<InvoiceDto> PurchaseInvoices { get; } = new();

    public MainViewModel(IInvoiceApiClient invoiceApiClient, IWindowNavigationService navigationService, ITokenStore tokenStore)
    {
        _invoiceApiClient = invoiceApiClient;
        _navigationService = navigationService;
        _tokenStore = tokenStore;
        _tokenStore.TokenChanged += OnTokenChanged;
        
    }

    private async Task<int> RefreshInvoicesAsync(InvoiceDirection direction)
    {
        var invoices = await _invoiceApiClient.GetInvoicesAsync(DateOnly.FromDateTime(DateFrom), DateOnly.FromDateTime(DateTo), direction);

        var targetCollection = direction == InvoiceDirection.Sales ? SalesInvoices : PurchaseInvoices;

        targetCollection.Clear();

        foreach (var invoice in invoices)
            targetCollection.Add(invoice);

        return targetCollection.Count;
    }

    [RelayCommand]
    private void CleanupOnClose()
    {
        Cleanup();
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
                Status = $"Limit KSeF. Spróbuj ponownie za około {Math.Ceiling(remaining.TotalMinutes):0} min.";
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

            var count = await RefreshInvoicesAsync(direction);

            Status = $"Pobrano {count} faktur z bazy danych.";
        }
        catch (KsefRateLimitException ex)
        {
            if (ex.RetryAfter is TimeSpan delay)
            {
                _rateLimitUntil = DateTimeOffset.UtcNow + delay;

                Status = $"Limit KSeF. Spróbuj ponownie za około {Math.Ceiling(delay.TotalMinutes):0} min.";
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

    [RelayCommand]
    private async Task ImportFromKsefAsync()
    {
        if (IsLoading)
            return;

        if (!IsLoggedIn)
        {
            Status = "Najpierw zaloguj się do aplikacji.";
            return;
        }

        if (DateFrom.Date > DateTo.Date)
        {
            Status = "Data początkowa nie może być późniejsza niż końcowa.";
            return;
        }

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
            Status = "Trwa import faktur z KSeF. Proszę czekać...";

            var result = await _invoiceApiClient.ImportFromKsefAsync(DateOnly.FromDateTime(DateFrom), DateOnly.FromDateTime(DateTo), direction);

            var count = await RefreshInvoicesAsync(direction);

            Status = $"Import zakończony. Zaimportowano: {result.ImportedCount}, pominięto: {result.SkippedCount}. Faktur na liście: {count}.";
        }
        catch (Exception ex)
        {
            Status = $"Błąd importu KSeF: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadInvoicePdfAsync()
    {
        // Anulujemy poprzednie pobieranie PDF.
        _invoicePdfCancellationTokenSource?.Cancel();

        var cancellationTokenSource = new CancellationTokenSource();
        _invoicePdfCancellationTokenSource = cancellationTokenSource;

        var cancellationToken = cancellationTokenSource.Token;

        InvoiceDto? selectedInvoice = SelectedTabIndex switch
        {
            0 => SelectedSalesInvoice,
            1 => SelectedPurchaseInvoice,
            _ => null
        };

        if (selectedInvoice is null)
            return;

        try
        {
            Status = "Pobieranie PDF faktury...";

            byte[] pdfBytes = await _invoiceApiClient.GetInvoicePdfAsync(
                selectedInvoice.KsefNumber,
                cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();

            string pdfPath = Path.Combine(
    Path.GetTempPath(),
    $"{Guid.NewGuid():N}.pdf");

            try
            {
                await File.WriteAllBytesAsync(
                    pdfPath,
                    pdfBytes,
                    cancellationToken);

                cancellationToken.ThrowIfCancellationRequested();
            }
            catch
            {
                // Usuwamy niekompletny lub niepotrzebny plik.
                if (File.Exists(pdfPath))
                {
                    try
                    {
                        File.Delete(pdfPath);
                    }
                    catch (IOException)
                    {
                        // Nie udało się usunąć pliku.
                    }
                    catch (UnauthorizedAccessException)
                    {
                        // Brak możliwości usunięcia pliku.
                    }
                }

                throw;
            }

            // Usuwamy poprzedni PDF.
            DeletePreviousTemporaryPdf();

            // Zapamiętujemy nowy plik.
            _currentTemporaryPdfPath = pdfPath;

            // Wyświetlamy nowy dokument.
            SelectedInvoicePdfPath = pdfPath;

            Status = $"Wyświetlono PDF faktury {selectedInvoice.InvoiceNumber}.";
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            // Poprzednie pobieranie zostało anulowane.
        }
        catch (Exception ex)
        {
            if (!cancellationToken.IsCancellationRequested)
                Status = $"Błąd pobierania PDF: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(
                _invoicePdfCancellationTokenSource,
                cancellationTokenSource))
            {
                _invoicePdfCancellationTokenSource = null;
            }

            cancellationTokenSource.Dispose();
        }
    }

    partial void OnSelectedSalesInvoiceChanged(InvoiceDto? value)
    {
        _invoicePdfCancellationTokenSource?.Cancel();

        if (value is null)
        {
            DeletePreviousTemporaryPdf();
            return;
        }

        LoadInvoiceXmlCommand.Execute(null);
        LoadInvoicePdfCommand.Execute(null);
    }

    private void OnTokenChanged(object? sender, EventArgs e)
    {
        System.Windows.Application.Current.Dispatcher.Invoke(() =>
        {
            IsLoggedIn = !string.IsNullOrWhiteSpace(_tokenStore.AccessToken);
        });
    }

    private void DeletePreviousTemporaryPdf()
    {
        if (_currentTemporaryPdfPath is null)
            return;

        string path = _currentTemporaryPdfPath;

        // Najpierw odłączamy dokument od PDFViewer.
        SelectedInvoicePdfPath = null;

        try
        {
            if (File.Exists(path))
                File.Delete(path);

            _currentTemporaryPdfPath = null;
        }
        catch (IOException)
        {
            // PDFViewer może jeszcze korzystać z pliku.
            // Zachowujemy ścieżkę, aby móc ponowić usuwanie.
        }
        catch (UnauthorizedAccessException)
        {
            // Nie udało się usunąć pliku.
            // Zachowujemy ścieżkę.
        }
    }

    public void Cleanup()
    {
        // Anulujemy ewentualne pobieranie PDF.
        _invoicePdfCancellationTokenSource?.Cancel();

        // Odłączamy dokument od kontrolki PDFViewer
        // i usuwamy plik tymczasowy.
        DeletePreviousTemporaryPdf();
    }

}