using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;
using System.IO;
using xKsef.Application.Credentials;
using xKsef.Contracts.Invoices;
using xKsef.Contracts.Tenants;
using xKsef.Wpf.Authentication;
using xKsef.Wpf.Exceptions;
using xKsef.Wpf.Http;
using xKsef.Wpf.Models;
using xKsef.Wpf.Services;
using xKsef.Wpf.Services.Certificates;

namespace xKsef.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly record struct ContextSnapshot(long Generation, long SessionVersion, Guid? TenantId, CancellationToken CancellationToken);

    private readonly IAuthenticationApiClient _authenticationApiClient;
    private readonly ICertificateSelectionWindowService _certificateSelectionWindowService;
    private readonly IInvoiceApiClient _invoiceApiClient;
    private DateTimeOffset? _rateLimitUntil;
    private readonly IWindowNavigationService _navigationService;
    private readonly IKsefCredentialsApiClient _ksefCredentialsApiClient;
    private readonly ITokenStore _tokenStore;
    private readonly ITenantApiClient _tenantApiClient;
    private readonly TenantContext _tenantContext;
    private readonly object _contextSync = new();
    private long _contextGeneration;
    private long _contextSessionVersion;
    private long _contextSessionUiScheduledVersion;
    private long _contextSessionUiHandledVersion;
    private Guid? _contextTenantId;
    private CancellationTokenSource _contextCancellationTokenSource = new();
    private bool _isCleanedUp;
    private bool _isClearingContextDependentState;
    private long _certificateSaveOperationId;
    private CancellationTokenSource? _invoicePdfCancellationTokenSource;
    private long _invoicePdfRequestId;
    private readonly record struct InvoiceFilterSnapshot(DateOnly From, DateOnly To, InvoiceDateKind DateKind, string? TimeZoneId);
    private long _invoiceFilterVersion;
    private long _invoiceReadOperationId;
    private long? _invoiceLoadingOwnerId;
    private bool _invoiceOperationIsImport;
    private CancellationTokenSource? _invoiceReadCancellationTokenSource;
    private readonly HashSet<string> _temporaryPdfPathsPendingDeletion = [];
    private readonly IKsefTokenWindowService _ksefTokenWindowService;
    public string KsefTokenStatus => HasKsefToken ? "Token KSeF skonfigurowany" : "Token KSeF nieskonfigurowany";

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
    public ObservableCollection<InvoiceCurrencySummaryDto> SalesSummaries { get; } = new();
    public ObservableCollection<InvoiceCurrencySummaryDto> PurchaseSummaries { get; } = new();

    [ObservableProperty]
    private InvoiceDateKind _selectedInvoiceDateKind = InvoiceDateKind.IssueDate;
    [ObservableProperty]
    private bool _hasSalesQueryResult;
    [ObservableProperty]
    private bool _hasPurchaseQueryResult;

    public ObservableCollection<TenantDto> AvailableTenants { get; } = new();

    public ObservableCollection<KsefCredentialOption> AvailableKsefCredentials { get; } = new();

    [ObservableProperty]
    private KsefCredentialOption? _selectedKsefCredential;


    [ObservableProperty]
    private TenantDto? selectedTenant;

    [ObservableProperty]
    private bool _hasKsefToken;

    [ObservableProperty]
    private Guid? _ksefCredentialId;
   
    [ObservableProperty]
    private bool _hasKsefCertificate;

    [ObservableProperty]
    private Guid? _ksefCertificateCredentialId;

    [ObservableProperty]
    private Guid? _preferredKsefCredentialId;
    public MainViewModel(IInvoiceApiClient invoiceApiClient, IWindowNavigationService navigationService, ITokenStore tokenStore, ITenantApiClient tenantApiClient, TenantContext tenantContext, ICertificateSelectionWindowService certificateSelectionWindowService, IAuthenticationApiClient authenticationApiClient, IKsefCredentialsApiClient ksefCredentialsApiClient, IKsefTokenWindowService ksefTokenWindowService)
    {
        _invoiceApiClient = invoiceApiClient;
        _navigationService = navigationService;
        _tokenStore = tokenStore;
        _tenantApiClient = tenantApiClient;
        _tenantContext = tenantContext;
        _contextTenantId = tenantContext.TenantId;
        _tenantContext.TenantChanged += OnTenantChanged;
        _certificateSelectionWindowService = certificateSelectionWindowService;
        _authenticationApiClient = authenticationApiClient;
        _ksefCredentialsApiClient = ksefCredentialsApiClient;
        _ksefTokenWindowService = ksefTokenWindowService;
        _tokenStore.TokenChanged += OnTokenChanged;

        var initialSessionSnapshot = _tokenStore.GetSessionSnapshot();
        AdvanceSessionContext(initialSessionSnapshot.SessionVersion);
    }

    private void OnTenantChanged(object? sender, EventArgs e)
    {
        if (!AdvanceContextGeneration(_tenantContext.TenantId, tenantChanged: true, out long eventGeneration)) return;

        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (!IsCurrentContextGeneration(eventGeneration)) return;

            ClearContextDependentState();

            if (!IsLoggedIn || !_tenantContext.HasTenant) return;

            var contextSnapshot = CaptureContext(ApiRequestScope.TenantHeader);
            if (contextSnapshot is not ApiRequestContext context || !HasTenantHeaderContext(context)) return;

            await CheckKsefTokenAsync(context);
            if (!IsCurrentContext(context)) return;

            await CheckKsefCertificateAsync(context);
            if (!IsCurrentContext(context)) return;

            await CheckPreferredKsefCredentialAsync(context);
            if (!IsCurrentContext(context)) return;

            RefreshKsefCredentials();
        });
    }

    private async Task CheckKsefTokenAsync(ApiRequestContext context)
    {
        if (!IsCurrentContext(context)) return;

        if (context.TenantId is null || string.IsNullOrWhiteSpace(context.AccessToken)) return;

        try
        {
            KsefTokenInfo tokenInfo = await _ksefCredentialsApiClient.GetTokenInfoAsync(context);
            if (!IsCurrentContext(context)) return;

            HasKsefToken = tokenInfo.Exists;
            KsefCredentialId = tokenInfo.CredentialId;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentContext(context))
                Status = $"Błąd sprawdzania tokenu KSeF: {ex.Message}";
        }
    }

    private async Task CheckKsefCertificateAsync(ApiRequestContext context)
    {
        if (!IsCurrentContext(context)) return;

        if (context.TenantId is null || string.IsNullOrWhiteSpace(context.AccessToken)) return;

        try
        {
            KsefTokenInfo certificateInfo = await _ksefCredentialsApiClient.GetCertificateInfoAsync(context);
            if (!IsCurrentContext(context)) return;

            HasKsefCertificate = certificateInfo.Exists;
            KsefCertificateCredentialId = certificateInfo.CredentialId;
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentContext(context))
                Status = $"Błąd sprawdzania certyfikatu KSeF: {ex.Message}";
        }
    }
    private async Task LoadTenantsAsync()
    {
        try
        {
            var tenants = await _tenantApiClient.GetMyTenantsAsync();

            AvailableTenants.Clear();

            foreach (var tenant in tenants)
                AvailableTenants.Add(tenant);

            SelectedTenant = AvailableTenants.FirstOrDefault();

            _tenantContext.SetTenant(SelectedTenant);
            if (SelectedTenant is null)
            {
                Status = "Nie masz dostępu do żadnego podmiotu.";
                return;
            }

            Status = $"Wybrano podmiot: {SelectedTenant.Name}";

     
        }
        catch (Exception ex)
        {
            AvailableTenants.Clear();
            SelectedTenant = null;
            _tenantContext.SetTenant(null);
            Status = $"Błąd pobierania podmiotów: {ex.Message}";
        }
    }

    private void RefreshKsefCredentials()
    {
        AvailableKsefCredentials.Clear();
        SelectedKsefCredential = null;

        if (HasKsefToken && KsefCredentialId is Guid tokenId)
            AvailableKsefCredentials.Add(new KsefCredentialOption { CredentialId = tokenId, Name = "Token KSeF" });

        if (HasKsefCertificate && KsefCertificateCredentialId is Guid certificateId)
            AvailableKsefCredentials.Add(new KsefCredentialOption { CredentialId = certificateId, Name = "Certyfikat KSeF" });

        SelectedKsefCredential = AvailableKsefCredentials.FirstOrDefault(x => x.CredentialId == PreferredKsefCredentialId);
    }

    private async Task CheckPreferredKsefCredentialAsync(ApiRequestContext context)
    {
        if (!IsCurrentContext(context)) return;

        if (context.TenantId is not Guid tenantId)
            return;

        try
        {
            Guid? preferredCredentialId = await _tenantApiClient.GetPreferredKsefCredentialIdAsync(tenantId);
            if (!IsCurrentContext(context)) return;

            PreferredKsefCredentialId = preferredCredentialId;
        }
        catch (Exception ex)
        {
            if (IsCurrentContext(context))
                Status = $"Błąd odczytu preferowanego poświadczenia KSeF: {ex.Message}";
        }
    }

    partial void OnDateFromChanged(DateTime value) => InvalidateInvoiceFilter();
    partial void OnDateToChanged(DateTime value) => InvalidateInvoiceFilter();
    partial void OnSelectedInvoiceDateKindChanged(InvoiceDateKind value) => InvalidateInvoiceFilter();

    private void InvalidateInvoiceFilter()
    {
        _invoiceFilterVersion++;
        _invoiceReadCancellationTokenSource?.Cancel();
        bool releaseLoading = _invoiceLoadingOwnerId.HasValue && !_invoiceOperationIsImport;
        if (releaseLoading)
        {
            _invoiceReadOperationId++;
            _invoiceLoadingOwnerId = null;
        }
        ClearInvoiceResults();
        if (releaseLoading) IsLoading = false;
    }

    private void ClearInvoiceResults()
    {
        _invoicePdfRequestId++;
        _invoicePdfCancellationTokenSource?.Cancel();
        bool wasClearing = _isClearingContextDependentState;
        _isClearingContextDependentState = true;
        try
        {
            HasSalesQueryResult = false;
            HasPurchaseQueryResult = false;
            SalesInvoices.Clear();
            PurchaseInvoices.Clear();
            SalesSummaries.Clear();
            PurchaseSummaries.Clear();
            SelectedSalesInvoice = null;
            SelectedPurchaseInvoice = null;
        }
        finally { _isClearingContextDependentState = wasClearing; }
        DeletePreviousTemporaryPdf();
    }

    private InvoiceFilterSnapshot CaptureInvoiceFilter()
    {
        DateOnly from = DateOnly.FromDateTime(DateFrom);
        DateOnly to = DateOnly.FromDateTime(DateTo);
        if (from > to) throw new InvalidOperationException("Data początkowa nie może być późniejsza niż końcowa.");
        if (!Enum.IsDefined(SelectedInvoiceDateKind)) throw new InvalidOperationException("Nieprawidłowy rodzaj daty.");
        string? zoneId = null;
        if (SelectedInvoiceDateKind == InvoiceDateKind.InvoicingDate)
        {
            try
            {
                var zone = TimeZoneInfo.Local;
                zoneId = TimeZoneInfo.TryConvertWindowsIdToIanaId(zone.Id, out string? ianaId) ? ianaId : zone.Id;
                _ = TimeZoneInfo.FindSystemTimeZoneById(zoneId!);
            }
            catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException or ArgumentException)
            {
                throw new InvalidOperationException("Nie udało się ustalić obsługiwanej strefy czasowej użytkownika. Sprawdź ustawienia strefy czasowej systemu.", ex);
            }
        }
        return new InvoiceFilterSnapshot(from, to, SelectedInvoiceDateKind, zoneId);
    }

    private bool OwnsInvoiceLoading(ApiRequestContext context, long operationId) => IsCurrentContext(context) && _invoiceLoadingOwnerId == operationId && _invoiceReadOperationId == operationId;

    private bool IsCurrentInvoiceRead(ApiRequestContext context, long operationId, long filterVersion, InvoiceFilterSnapshot filter)
    {
        return OwnsInvoiceLoading(context, operationId) && filterVersion == _invoiceFilterVersion
            && filter.From == DateOnly.FromDateTime(DateFrom) && filter.To == DateOnly.FromDateTime(DateTo) && filter.DateKind == SelectedInvoiceDateKind;
    }

    private void FinishInvoiceLoading(ApiRequestContext context, long operationId)
    {
        if (!OwnsInvoiceLoading(context, operationId)) return;
        _invoiceLoadingOwnerId = null;
        IsLoading = false;
    }

    private async Task<bool> RefreshInvoicesAsync(ApiRequestContext context, InvoiceDirection direction, long operationId)
    {
        var filter = CaptureInvoiceFilter();
        long filterVersion = _invoiceFilterVersion;
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        _invoiceReadCancellationTokenSource = cancellation;
        var readContext = context with { CancellationToken = cancellation.Token };
        bool IsCurrent() => IsCurrentInvoiceRead(readContext, operationId, filterVersion, filter);
        try
        {
            var result = await _invoiceApiClient.QueryInvoicesAsync(readContext, filter.From, filter.To, direction, filter.DateKind, filter.TimeZoneId);
            if (!IsCurrent()) return false;
            var invoices = direction == InvoiceDirection.Sales ? SalesInvoices : PurchaseInvoices;
            var summaries = direction == InvoiceDirection.Sales ? SalesSummaries : PurchaseSummaries;
            bool wasClearing = _isClearingContextDependentState;
            _isClearingContextDependentState = true;
            try
            {
                if (direction == InvoiceDirection.Sales) HasSalesQueryResult = false; else HasPurchaseQueryResult = false;
                if (!IsCurrent()) return false;
                invoices.Clear();
                if (!IsCurrent()) return false;
                summaries.Clear();
                if (!IsCurrent()) return false;
                if (direction == InvoiceDirection.Sales) SelectedSalesInvoice = null; else SelectedPurchaseInvoice = null;
                foreach (var invoice in result.Items)
                {
                    if (!IsCurrent()) return false;
                    invoices.Add(invoice);
                }
                foreach (var summary in result.Summaries)
                {
                    if (!IsCurrent()) return false;
                    summaries.Add(summary);
                }
                if (!IsCurrent()) return false;
                if (direction == InvoiceDirection.Sales) HasSalesQueryResult = true; else HasPurchaseQueryResult = true;
            }
            finally { _isClearingContextDependentState = wasClearing; }
            if (!IsCurrent()) return false;
            if ((direction == InvoiceDirection.Sales && SelectedTabIndex == 0) || (direction == InvoiceDirection.Purchase && SelectedTabIndex == 1))
                HandleActiveInvoiceChanged();
            return IsCurrent();
        }
        catch (Exception) when (!IsCurrent()) { return false; }
        finally
        {
            if (ReferenceEquals(_invoiceReadCancellationTokenSource, cancellation)) _invoiceReadCancellationTokenSource = null;
        }
    }

    [RelayCommand]
    private void CleanupOnClose()
    {
        Cleanup();
    }

    [RelayCommand]
    private async Task NewTenantAsync()
    {
        if (!IsLoggedIn) return;
        TenantCreationDialogResult? result = _navigationService.ShowNewTenantWindow();
        if (result is null) return;

        bool IsSessionCurrent() => !_isCleanedUp && _tokenStore.SessionVersion == result.SessionVersion;
        if (!IsSessionCurrent()) return;
        var snapshot = CaptureContext(ApiRequestScope.User);
        if (snapshot is not ApiRequestContext context || context.SessionVersion != result.SessionVersion || string.IsNullOrWhiteSpace(context.AccessToken)) return;
        var userContext = context with { TenantId = null };

        try
        {
            IReadOnlyList<TenantDto> tenants = await _tenantApiClient.GetMyTenantsAsync(userContext);
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!IsSessionCurrent()) return;
            TenantDto? createdTenant = tenants.FirstOrDefault(x => x.Id == result.TenantId);
            if (createdTenant is not null)
            {
                if (!IsSessionCurrent()) return;
                AvailableTenants.Clear();
                foreach (TenantDto tenant in tenants)
                {
                    if (!IsSessionCurrent()) return;
                    AvailableTenants.Add(tenant);
                }
                if (!IsSessionCurrent()) return;
                SelectedTenant = createdTenant;
            }
            if (IsSessionCurrent()) Status = "Tenant został utworzony.";
        }
        catch (Exception)
        {
            if (IsSessionCurrent())
                Status = "Tenant został utworzony, ale nie udało się odświeżyć listy tenantów.";
        }
    }

    [RelayCommand]
    private void NewUser()
    {
        if (!IsLoggedIn) return;
        _navigationService.ShowNewUserWindow();
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
    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadInvoicesAsync()
    {
        if (IsLoading) return;
        var snapshot = CaptureContext(ApiRequestScope.TenantHeader);
        if (snapshot is not ApiRequestContext context || !HasTenantHeaderContext(context) || !IsCurrentContext(context)) return;
        if (_rateLimitUntil is DateTimeOffset until && until > DateTimeOffset.UtcNow)
        {
            Status = $"Limit KSeF. Spróbuj ponownie za około {Math.Ceiling((until - DateTimeOffset.UtcNow).TotalMinutes):0} min.";
            return;
        }
        _rateLimitUntil = null;
        long operationId = ++_invoiceReadOperationId;
        _invoiceLoadingOwnerId = operationId;
        _invoiceOperationIsImport = false;
        IsLoading = true;
        Status = "Pobieranie faktur...";
        try
        {
            var direction = SelectedTabIndex switch { 0 => InvoiceDirection.Sales, 1 => InvoiceDirection.Purchase, _ => throw new InvalidOperationException("Nieznana zakładka faktur.") };
            if (await RefreshInvoicesAsync(context, direction, operationId) && OwnsInvoiceLoading(context, operationId))
                Status = $"Pobrano {(direction == InvoiceDirection.Sales ? SalesInvoices.Count : PurchaseInvoices.Count)} faktur z bazy danych.";
        }
        catch (KsefRateLimitException ex)
        {
            if (!OwnsInvoiceLoading(context, operationId)) return;
            if (ex.RetryAfter is TimeSpan delay)
            {
                _rateLimitUntil = DateTimeOffset.UtcNow + delay;
                Status = $"Limit KSeF. Spróbuj ponownie za około {Math.Ceiling(delay.TotalMinutes):0} min.";
            }
            else Status = "Przekroczono limit KSeF. Spróbuj ponownie później.";
        }
        catch (Exception ex)
        {
            if (OwnsInvoiceLoading(context, operationId)) Status = $"Błąd: {ex.Message}";
        }
        finally { FinishInvoiceLoading(context, operationId); }
    }

    [RelayCommand]
    private async Task ConfigureKsefTokenAsync()
    {
        if (!IsLoggedIn || !_tenantContext.HasTenant) return;

        var contextSnapshot = CaptureContext(ApiRequestScope.TenantHeader);
        if (contextSnapshot is not ApiRequestContext context || !HasTenantHeaderContext(context)) return;

        string? token = _ksefTokenWindowService.ShowTokenDialog();

        if (string.IsNullOrWhiteSpace(token)) return;

        try
        {
            if (!IsCurrentContext(context)) return;

            Guid credentialId = await _ksefCredentialsApiClient.SaveTokenAsync(context, token);
            if (!IsCurrentContext(context)) return;

            HasKsefToken = true;
            KsefCredentialId = credentialId;
            RefreshKsefCredentials();

            Status = "Token KSeF został zapisany.";
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentContext(context))
                Status = $"Błąd zapisywania tokenu KSeF: {ex.Message}";
        }
        finally
        {
            token = null;
        }
    }

    [RelayCommand]
    private async Task ImportFromKsefAsync()
    {
        if (IsLoading) return;
        if (!IsLoggedIn) { Status = "Najpierw zaloguj się do aplikacji."; return; }
        var snapshot = CaptureContext(ApiRequestScope.TenantHeader);
        if (snapshot is not ApiRequestContext context || !HasTenantHeaderContext(context) || !IsCurrentContext(context)) return;
        if (DateFrom.Date > DateTo.Date) { Status = "Data początkowa nie może być późniejsza niż końcowa."; return; }
        var direction = SelectedTabIndex switch { 0 => InvoiceDirection.Sales, 1 => InvoiceDirection.Purchase, _ => throw new InvalidOperationException("Nieznana zakładka faktur.") };
        DateOnly from = DateOnly.FromDateTime(DateFrom);
        DateOnly to = DateOnly.FromDateTime(DateTo);
        long operationId = ++_invoiceReadOperationId;
        _invoiceLoadingOwnerId = operationId;
        _invoiceOperationIsImport = true;
        IsLoading = true;
        Status = "Trwa import faktur z KSeF. Proszę czekać...";
        InvoicePackageReadResult? imported = null;
        try
        {
            imported = await _invoiceApiClient.ImportFromKsefAsync(context, from, to, direction);
            if (!OwnsInvoiceLoading(context, operationId)) return;
            bool refreshed = await RefreshInvoicesAsync(context, direction, operationId);
            if (!OwnsInvoiceLoading(context, operationId)) return;
            Status = refreshed
                ? $"Import zakończony. Zaimportowano: {imported.ImportedCount}, pominięto: {imported.SkippedCount}. Faktur na liście: {(direction == InvoiceDirection.Sales ? SalesInvoices.Count : PurchaseInvoices.Count)}."
                : "Import zakończony poprawnie. Filtr zmienił się podczas odświeżania. Pobierz faktury ponownie.";
        }
        catch (Exception ex)
        {
            if (OwnsInvoiceLoading(context, operationId))
                Status = imported is null ? $"Import nie powiódł się: {ex.Message}" : $"Import zakończony poprawnie, ale odświeżenie listy nie powiodło się: {ex.Message}";
        }
        finally { FinishInvoiceLoading(context, operationId); }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task LoadInvoicePdfAsync()
    {
        var contextSnapshot = CaptureContext(ApiRequestScope.TenantHeader);
        if (contextSnapshot is not ApiRequestContext context || !HasTenantHeaderContext(context)) return;

        InvoiceDto? selectedInvoice = GetActiveInvoice();
        if (selectedInvoice is null) return;

        long requestId = _invoicePdfRequestId;
        var cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(context.CancellationToken);
        _invoicePdfCancellationTokenSource = cancellationTokenSource;
        var pdfContext = context with { CancellationToken = cancellationTokenSource.Token };

        try
        {
            if (!IsCurrentInvoicePdfRequest(requestId, selectedInvoice, pdfContext)) return;

            Status = "Pobieranie PDF faktury...";

            byte[] pdfBytes = await _invoiceApiClient.GetInvoicePdfAsync(
                pdfContext,
                selectedInvoice.KsefNumber);

            pdfContext.CancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentInvoicePdfRequest(requestId, selectedInvoice, pdfContext)) return;

            string pdfPath = Path.Combine(
    Path.GetTempPath(),
    $"{Guid.NewGuid():N}.pdf");

            try
            {
                await File.WriteAllBytesAsync(
                    pdfPath,
                    pdfBytes,
                    pdfContext.CancellationToken);

                pdfContext.CancellationToken.ThrowIfCancellationRequested();
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

            if (!IsCurrentInvoicePdfRequest(requestId, selectedInvoice, pdfContext))
            {
                DeleteUnboundTemporaryPdf(pdfPath);
                return;
            }

            _currentTemporaryPdfPath = pdfPath;
            SelectedInvoicePdfPath = pdfPath;

            Status = $"Wyświetlono PDF faktury {selectedInvoice.InvoiceNumber}.";
        }
        catch (OperationCanceledException) when (pdfContext.CancellationToken.IsCancellationRequested)
        {
            // Poprzednie pobieranie zostało anulowane.
        }
        catch (Exception ex)
        {
            if (IsCurrentInvoicePdfRequest(requestId, selectedInvoice, pdfContext))
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

    [RelayCommand]
    private async Task AuthenticateKsefTokenAsync()
    {
        if (!IsLoggedIn || !_tenantContext.HasTenant)
        {
            Status = "Najpierw zaloguj się i wybierz podmiot.";
            return;
        }

        if (IsLoading) return;

        if (!HasKsefToken || KsefCredentialId is null)
        {
            Status = "Najpierw skonfiguruj token KSeF.";
            return;
        }

        var contextSnapshot = CaptureContext(ApiRequestScope.TenantHeader);
        if (contextSnapshot is not ApiRequestContext context || !HasTenantHeaderContext(context)) return;

        try
        {
            if (!IsCurrentContext(context)) return;

            IsLoading = true;
            Status = "Trwa uwierzytelnianie tokenem KSeF...";

            await _authenticationApiClient.AuthenticateKsefTokenAsync(context);
            if (!IsCurrentContext(context)) return;

            Status = "Uwierzytelnianie tokenem KSeF zakończone pomyślnie.";
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentContext(context))
                Status = $"Błąd uwierzytelniania tokenem KSeF: {ex.Message}";
        }
        finally
        {
            if (IsCurrentContext(context))
                IsLoading = false;
        }
    }

    [RelayCommand]
    private async Task SavePreferredKsefCredentialAsync()
    {
        if (!IsLoggedIn || !_tenantContext.HasTenant)
        {
            Status = "Najpierw zaloguj się i wybierz podmiot.";
            return;
        }

        if (SelectedKsefCredential is null)
        {
            Status = "Wybierz token lub certyfikat KSeF.";
            return;
        }

        if (IsLoading) return;

        try
        {
            IsLoading = true;

            Guid tenantId = _tenantContext.TenantId ?? throw new InvalidOperationException("Nie wybrano podmiotu.");
            Guid credentialId = SelectedKsefCredential.CredentialId;

            await _tenantApiClient.SetPreferredKsefCredentialIdAsync(tenantId, credentialId);

            PreferredKsefCredentialId = credentialId;

            Status = $"Zapisano preferowane poświadczenie KSeF: {SelectedKsefCredential.Name}.";
        }
        catch (Exception ex)
        {
            Status = $"Błąd zapisywania preferowanego poświadczenia KSeF: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }

    partial void OnSelectedSalesInvoiceChanged(InvoiceDto? value)
    {
        if (!_isClearingContextDependentState && SelectedTabIndex == 0)
            HandleActiveInvoiceChanged();
    }

    partial void OnSelectedPurchaseInvoiceChanged(InvoiceDto? value)
    {
        if (!_isClearingContextDependentState && SelectedTabIndex == 1)
            HandleActiveInvoiceChanged();
    }

    partial void OnSelectedTabIndexChanged(int value)
    {
        HandleActiveInvoiceChanged();
    }

    private InvoiceDto? GetActiveInvoice() => SelectedTabIndex switch
    {
        0 => SelectedSalesInvoice,
        1 => SelectedPurchaseInvoice,
        _ => null
    };

    private void HandleActiveInvoiceChanged()
    {
        _invoicePdfRequestId++;
        _invoicePdfCancellationTokenSource?.Cancel();
        DeletePreviousTemporaryPdf();

        if (GetActiveInvoice() is not null)
            LoadInvoicePdfCommand.Execute(null);
    }

    private bool IsCurrentInvoicePdfRequest(long requestId, InvoiceDto invoice, ApiRequestContext context)
    {
        return IsCurrentContext(context)
            && !context.CancellationToken.IsCancellationRequested
            && requestId == _invoicePdfRequestId
            && ReferenceEquals(GetActiveInvoice(), invoice);
    }

    private static bool HasTenantHeaderContext(ApiRequestContext context)
    {
        return context.Scope == ApiRequestScope.TenantHeader
            && !string.IsNullOrWhiteSpace(context.AccessToken)
            && context.TenantId is Guid tenantId
            && tenantId != Guid.Empty;
    }

    private ApiRequestContext? CaptureContext(ApiRequestScope scope)
    {
        var sessionBefore = _tokenStore.GetSessionSnapshot();
        ContextSnapshot contextSnapshot;

        lock (_contextSync)
        {
            if (_isCleanedUp || sessionBefore.SessionVersion != _contextSessionVersion)
                return null;

            contextSnapshot = new ContextSnapshot(
                _contextGeneration,
                _contextSessionVersion,
                _contextTenantId,
                _contextCancellationTokenSource.Token);
        }

        var sessionAfter = _tokenStore.GetSessionSnapshot();
        if (sessionAfter.SessionVersion != sessionBefore.SessionVersion)
            return null;

        lock (_contextSync)
        {
            if (_isCleanedUp
                || contextSnapshot.Generation != _contextGeneration
                || contextSnapshot.SessionVersion != _contextSessionVersion
                || contextSnapshot.TenantId != _contextTenantId
                || contextSnapshot.CancellationToken.IsCancellationRequested)
                return null;
        }

        return new ApiRequestContext(
            contextSnapshot.Generation,
            sessionBefore.SessionVersion,
            contextSnapshot.TenantId,
            sessionBefore.AccessToken,
            contextSnapshot.CancellationToken,
            scope);
    }

    private bool IsCurrentContext(ApiRequestContext context)
    {
        lock (_contextSync)
        {
            return !_isCleanedUp
                && !context.CancellationToken.IsCancellationRequested
                && context.Generation == _contextGeneration
                && context.SessionVersion == _contextSessionVersion
                && context.TenantId == _contextTenantId;
        }
    }

    private bool AdvanceContextGeneration(Guid? tenantId, bool tenantChanged, out long generation)
    {
        CancellationTokenSource previousCancellationTokenSource;

        lock (_contextSync)
        {
            if (_isCleanedUp)
            {
                generation = _contextGeneration;
                return false;
            }

            _contextGeneration++;
            if (tenantChanged)
                _contextTenantId = tenantId;

            generation = _contextGeneration;
            previousCancellationTokenSource = _contextCancellationTokenSource;
            _contextCancellationTokenSource = new CancellationTokenSource();
        }

        CancelAndDisposeContext(previousCancellationTokenSource);

        return true;
    }

    private bool AdvanceSessionContext(long sessionVersion)
    {
        CancellationTokenSource? previousCancellationTokenSource = null;

        lock (_contextSync)
        {
            if (_isCleanedUp || sessionVersion < _contextSessionVersion)
                return false;

            if (sessionVersion > _contextSessionVersion)
            {
                _contextSessionVersion = sessionVersion;
                _contextGeneration++;
                previousCancellationTokenSource = _contextCancellationTokenSource;
                _contextCancellationTokenSource = new CancellationTokenSource();
            }
        }

        if (previousCancellationTokenSource is not null)
            CancelAndDisposeContext(previousCancellationTokenSource);

        return true;
    }

    private void ScheduleSessionUi(long sessionVersion)
    {
        if (_tokenStore.GetSessionSnapshot().SessionVersion != sessionVersion)
            return;

        lock (_contextSync)
        {
            if (_isCleanedUp
                || _contextSessionVersion != sessionVersion
                || sessionVersion <= _contextSessionUiScheduledVersion)
                return;

            _contextSessionUiScheduledVersion = sessionVersion;
        }

        System.Windows.Application.Current.Dispatcher.InvokeAsync(async () =>
        {
            if (!TryMarkSessionUiHandled(sessionVersion, out var sessionSnapshot)) return;

            IsLoggedIn = !string.IsNullOrWhiteSpace(sessionSnapshot.AccessToken);
            ClearContextDependentState();
            AvailableTenants.Clear();
            SelectedTenant = null;
            _tenantContext.SetTenant(null);

            if (IsLoggedIn)
                await LoadTenantsAsync();
        });
    }

    private bool TryMarkSessionUiHandled(long sessionVersion, out (string? AccessToken, long SessionVersion) sessionSnapshot)
    {
        sessionSnapshot = _tokenStore.GetSessionSnapshot();
        if (sessionSnapshot.SessionVersion != sessionVersion)
            return false;

        lock (_contextSync)
        {
            if (_isCleanedUp
                || _contextSessionVersion != sessionVersion
                || _contextSessionUiHandledVersion >= sessionVersion)
                return false;

            _contextSessionUiHandledVersion = sessionVersion;
            return true;
        }
    }

    private bool IsCurrentContextGeneration(long generation)
    {
        lock (_contextSync)
            return !_isCleanedUp && generation == _contextGeneration;
    }

    private static void CancelAndDisposeContext(CancellationTokenSource cancellationTokenSource)
    {
        try
        {
            cancellationTokenSource.Cancel();
        }
        catch (AggregateException ex)
        {
            System.Diagnostics.Trace.TraceError("Context cancellation callback failed: {0}", ex);
        }
        finally
        {
            cancellationTokenSource.Dispose();
        }
    }

    private void OnTokenChanged(object? sender, EventArgs e)
    {
        if (e is not TokenChangedEventArgs tokenChangedEventArgs)
            return;

        long sessionVersion = tokenChangedEventArgs.SessionVersion;
        if (!AdvanceSessionContext(sessionVersion))
            return;

        ScheduleSessionUi(sessionVersion);
    }

    private void ClearContextDependentState()
    {
        _invoiceReadOperationId++;
        _invoiceLoadingOwnerId = null;
        _invoiceReadCancellationTokenSource?.Cancel();
        _invoiceReadCancellationTokenSource = null;
        ClearInvoiceResults();
        IsLoading = false;
        HasKsefToken = false;
        KsefCredentialId = null;
        HasKsefCertificate = false;
        KsefCertificateCredentialId = null;
        AvailableKsefCredentials.Clear();
        SelectedKsefCredential = null;
        PreferredKsefCredentialId = null;
    }

    partial void OnSelectedTenantChanged(TenantDto? value)
    {
        _tenantContext.SetTenant(value);
    }

    partial void OnHasKsefTokenChanged(bool value)
    {
        OnPropertyChanged(nameof(KsefTokenStatus));
    }

    private void DeletePreviousTemporaryPdf()
    {
        string? path = _currentTemporaryPdfPath;
        _currentTemporaryPdfPath = null;
        SelectedInvoicePdfPath = null;

        if (path is not null)
            _temporaryPdfPathsPendingDeletion.Add(path);

        if (_temporaryPdfPathsPendingDeletion.Count > 0)
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle,
                new Action(DeletePendingTemporaryPdfFiles));
    }

    private void DeletePendingTemporaryPdfFiles()
    {
        foreach (string path in _temporaryPdfPathsPendingDeletion.ToArray())
        {
            try
            {
                if (File.Exists(path))
                    File.Delete(path);

                _temporaryPdfPathsPendingDeletion.Remove(path);
            }
            catch (IOException)
            {
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private void DeleteUnboundTemporaryPdf(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (IOException)
        {
            _temporaryPdfPathsPendingDeletion.Add(path);
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle,
                new Action(DeletePendingTemporaryPdfFiles));
        }
        catch (UnauthorizedAccessException)
        {
            _temporaryPdfPathsPendingDeletion.Add(path);
            System.Windows.Application.Current.Dispatcher.BeginInvoke(
                System.Windows.Threading.DispatcherPriority.ContextIdle,
                new Action(DeletePendingTemporaryPdfFiles));
        }
    }

    [RelayCommand]
    private async Task ConfigureKsefCertificateAsync()
    {
        if (!IsLoggedIn || !_tenantContext.HasTenant)
        {
            Status = "Najpierw zaloguj się i wybierz firmę.";
            return;
        }

        var contextSnapshot = CaptureContext(ApiRequestScope.TenantHeader);
        if (contextSnapshot is not ApiRequestContext context || !HasTenantHeaderContext(context)) return;
        long operationId = Interlocked.Increment(ref _certificateSaveOperationId);

        var files = _certificateSelectionWindowService.SelectCertificate();
        if (files == null) return;
        if (!IsCurrentContext(context) || context.CancellationToken.IsCancellationRequested) return;

        try
        {
            if (!IsCurrentContext(context)) return;

            IsLoading = true;
            Status = "Zapisywanie certyfikatu KSeF...";

            var (certificatePem, encryptedPrivateKeyPem) = ReadCertificateFiles(files);
            context.CancellationToken.ThrowIfCancellationRequested();
            if (!IsCurrentContext(context)) return;

            Guid credentialId = await _authenticationApiClient.SaveKsefCertificateAsync(context, certificatePem, encryptedPrivateKeyPem, files.Password!);
            if (!IsCurrentContext(context)) return;

            HasKsefCertificate = true;
            KsefCertificateCredentialId = credentialId;
            RefreshKsefCredentials();
            Status = $"Certyfikat KSeF został zapisany. CredentialId: {credentialId}";
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception)
        {
            if (IsCurrentContext(context) && operationId == Volatile.Read(ref _certificateSaveOperationId))
                Status = "Nie udało się zapisać certyfikatu KSeF. Sprawdź poprawność plików i hasła oraz połączenie z serwerem.";
        }
        finally
        {
            if (IsCurrentContext(context) && operationId == Volatile.Read(ref _certificateSaveOperationId))
                IsLoading = false;
        }
    }
    private static (string CertificatePem, string EncryptedPrivateKeyPem) ReadCertificateFiles(KsefCertificateFiles files)
    {
        if (!File.Exists(files.CertificatePath)) throw new FileNotFoundException("Nie znaleziono pliku certyfikatu.", files.CertificatePath);
        if (!File.Exists(files.PrivateKeyPath)) throw new FileNotFoundException("Nie znaleziono pliku klucza prywatnego.", files.PrivateKeyPath);

        string certificatePem = File.ReadAllText(files.CertificatePath);
        string encryptedPrivateKeyPem = File.ReadAllText(files.PrivateKeyPath);

        return (certificatePem, encryptedPrivateKeyPem);
    }

    [RelayCommand]
    private async Task AuthenticateKsefCertificateAsync()
    {
        if (!IsLoggedIn || !_tenantContext.HasTenant)
        {
            Status = "Najpierw zaloguj się i wybierz podmiot.";
            return;
        }

        if (IsLoading) return;

        var contextSnapshot = CaptureContext(ApiRequestScope.TenantHeader);
        if (contextSnapshot is not ApiRequestContext context || !HasTenantHeaderContext(context)) return;

        try
        {
            if (!IsCurrentContext(context)) return;

            IsLoading = true;
            Status = "Uwierzytelnianie w KSeF za pomocą certyfikatu...";

            await _authenticationApiClient.AuthenticateKsefCertificateAsync(context);
            if (!IsCurrentContext(context)) return;

            Status = "Uwierzytelnianie certyfikatem KSeF zakończone pomyślnie.";
        }
        catch (OperationCanceledException) when (context.CancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception ex)
        {
            if (IsCurrentContext(context))
                Status = $"Błąd uwierzytelniania certyfikatem KSeF: {ex.Message}";
        }
        finally
        {
            if (IsCurrentContext(context))
                IsLoading = false;
        }
    }

    public void Cleanup()
    {
        CancellationTokenSource contextCancellationTokenSource;

        lock (_contextSync)
        {
            if (_isCleanedUp) return;

            _isCleanedUp = true;
            _contextGeneration++;
            _tokenStore.TokenChanged -= OnTokenChanged;
            _tenantContext.TenantChanged -= OnTenantChanged;
            contextCancellationTokenSource = _contextCancellationTokenSource;
        }

        CancelAndDisposeContext(contextCancellationTokenSource);
        _invoiceReadOperationId++;
        _invoiceLoadingOwnerId = null;
        _invoiceReadCancellationTokenSource?.Cancel();
        _invoiceReadCancellationTokenSource = null;

        _invoicePdfRequestId++;
        _invoicePdfCancellationTokenSource?.Cancel();
        DeletePreviousTemporaryPdf();
    }

}