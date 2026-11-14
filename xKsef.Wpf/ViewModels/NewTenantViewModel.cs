using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Net;
using System.Net.Http;
using xKsef.Wpf.Http;
using xKsef.Wpf.Models;

namespace xKsef.Wpf.ViewModels;

public sealed partial class NewTenantViewModel : ObservableObject, IDisposable
{
    private readonly ITenantApiClient _tenantApiClient;
    private readonly ITokenStore _tokenStore;
    private readonly ApiRequestContext _context;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private bool _isClosed;

    public event Action<bool?>? CloseRequested;
    public TenantCreationDialogResult? CreatedTenant { get; private set; }

    [ObservableProperty]
    private string _name = string.Empty;
    [ObservableProperty]
    private string _nip = string.Empty;
    [ObservableProperty]
    private string _taxEntityName = string.Empty;
    [ObservableProperty]
    private string _errorMessage = string.Empty;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSaving;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanEdit))]
    [NotifyCanExecuteChangedFor(nameof(SaveCommand))]
    private bool _isSessionValid;

    public bool CanEdit => IsSessionValid && !IsSaving && !_isClosed && CreatedTenant is null;

    public NewTenantViewModel(ITenantApiClient tenantApiClient, ITokenStore tokenStore)
    {
        _tenantApiClient = tenantApiClient;
        _tokenStore = tokenStore;
        var session = tokenStore.GetSessionSnapshot();
        _context = new ApiRequestContext(session.SessionVersion, session.SessionVersion, null, session.AccessToken, _cancellation.Token, ApiRequestScope.User);
        IsSessionValid = !string.IsNullOrWhiteSpace(session.AccessToken);
        _tokenStore.TokenChanged += OnTokenChanged;
        if (!IsCurrentSession()) InvalidateSession();
    }


    private bool IsCurrentSession() => !_isClosed && !_context.CancellationToken.IsCancellationRequested
        && _tokenStore.SessionVersion == _context.SessionVersion && !string.IsNullOrWhiteSpace(_context.AccessToken);

    [RelayCommand(CanExecute = nameof(CanEdit))]
    private async Task SaveAsync()
    {
        if (!CanEdit || !IsCurrentSession()) return;
        ErrorMessage = string.Empty;
        if (string.IsNullOrWhiteSpace(Name) || string.IsNullOrWhiteSpace(Nip) || string.IsNullOrWhiteSpace(TaxEntityName))
        {
            ErrorMessage = "Uzupełnij wszystkie pola.";
            return;
        }
        if (Nip.Length != 10 || Nip.Any(c => c < '0' || c > '9'))
        {
            ErrorMessage = "NIP musi składać się z 10 cyfr.";
            return;
        }
        if (Name.Length > 200 || TaxEntityName.Length > 200)
        {
            ErrorMessage = "Nazwa tenanta i podmiotu mogą mieć do 200 znaków.";
            return;
        }

        try
        {
            IsSaving = true;
            TenantCreationResult result = await _tenantApiClient.CreateTenantAsync(_context, Name.Trim(), Nip, TaxEntityName.Trim());
            if (!IsCurrentSession()) return;
            switch (result.StatusCode)
            {
                case HttpStatusCode.Created when result.TenantId is Guid tenantId && tenantId != Guid.Empty:
                    CreatedTenant = new TenantCreationDialogResult(tenantId, _context.SessionVersion);
                    CloseRequested?.Invoke(true);
                    break;
                case HttpStatusCode.BadRequest:
                    ErrorMessage = string.Join(Environment.NewLine, result.Errors.Select(TranslateError).Distinct());
                    if (string.IsNullOrWhiteSpace(ErrorMessage)) ErrorMessage = "Sprawdź poprawność danych tenanta.";
                    break;
                case HttpStatusCode.Unauthorized:
                    ErrorMessage = "Sesja wygasła. Zaloguj się ponownie.";
                    break;
                case HttpStatusCode.Forbidden:
                    ErrorMessage = "Brak dostępu do tworzenia tenanta. Zaloguj się ponownie.";
                    break;
                case HttpStatusCode.Conflict:
                    ErrorMessage = "Konflikt zapisu podmiotu o podanym NIP. Tenant nie został utworzony.";
                    break;
                default:
                    ErrorMessage = "Nie udało się utworzyć tenanta.";
                    break;
            }
        }
        catch (OperationCanceledException) when (_context.CancellationToken.IsCancellationRequested)
        {
        }
        catch (HttpRequestException)
        {
            if (IsCurrentSession()) ErrorMessage = "Nie można połączyć się z API.";
        }
        catch (Exception)
        {
            if (IsCurrentSession()) ErrorMessage = "Nie udało się utworzyć tenanta.";
        }
        finally
        {
            if (!_isClosed) IsSaving = false;
        }
    }

    private static string TranslateError(string code) => code switch
    {
        "InvalidName" => "Podaj nazwę tenanta (do 200 znaków).",
        "InvalidTaxEntityName" => "Podaj nazwę podmiotu podatkowego (do 200 znaków).",
        "InvalidNip" => "NIP musi składać się z 10 cyfr.",
        _ => "Sprawdź poprawność danych tenanta."
    };

    private void OnTokenChanged(object? sender, EventArgs e)
    {
        if (_uiContext is not null && SynchronizationContext.Current != _uiContext)
            _uiContext.Post(_ => InvalidateSession(), null);
        else
            InvalidateSession();
    }

    private void InvalidateSession()
    {
        if (_isClosed || _tokenStore.SessionVersion == _context.SessionVersion && !string.IsNullOrWhiteSpace(_context.AccessToken)) return;
        IsSessionValid = false;
        _cancellation.Cancel();
        ErrorMessage = "Sesja zmieniła się lub wygasła. Zamknij formularz i zaloguj się ponownie.";
    }

    [RelayCommand]
    private void Cancel() => CloseRequested?.Invoke(false);

    public void Dispose()
    {
        if (_isClosed) return;
        _isClosed = true;
        _tokenStore.TokenChanged -= OnTokenChanged;
        _cancellation.Cancel();
        IsSaving = false;
        IsSessionValid = false;
        _cancellation.Dispose();
    }
}
