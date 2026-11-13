using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Net;
using System.Net.Http;
using xKsef.Wpf.Authentication;
using xKsef.Wpf.Http;
using xKsef.Wpf.Models;

namespace xKsef.Wpf.ViewModels;

public sealed partial class NewUserViewModel : ObservableObject, IDisposable
{
    private readonly IAuthenticationApiClient _authenticationApiClient;
    private readonly ITokenStore _tokenStore;
    private readonly ApiRequestContext _context;
    private readonly CancellationTokenSource _cancellation = new();
    private readonly SynchronizationContext? _uiContext = SynchronizationContext.Current;
    private bool _isClosed;

    public event Action<bool?>? CloseRequested;

    [ObservableProperty]
    private string _login = string.Empty;
    [ObservableProperty]
    private string _email = string.Empty;
    [ObservableProperty]
    private string _password = string.Empty;
    [ObservableProperty]
    private string _confirmPassword = string.Empty;
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

    public bool CanEdit => IsSessionValid && !IsSaving && !_isClosed;

    public NewUserViewModel(IAuthenticationApiClient authenticationApiClient, ITokenStore tokenStore)
    {
        _authenticationApiClient = authenticationApiClient;
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
        if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrWhiteSpace(Email) || string.IsNullOrEmpty(Password))
        {
            ErrorMessage = "Podaj login, e-mail i hasło.";
            return;
        }
        if (Password != ConfirmPassword)
        {
            ErrorMessage = "Hasła nie są zgodne.";
            return;
        }

        try
        {
            IsSaving = true;
            UserCreationResult result = await _authenticationApiClient.CreateUserAsync(_context, Login.Trim(), Email.Trim(), Password);
            if (!IsCurrentSession()) return;

            switch (result.StatusCode)
            {
                case HttpStatusCode.Created:
                    CloseRequested?.Invoke(true);
                    break;
                case HttpStatusCode.BadRequest:
                    ErrorMessage = string.Join(Environment.NewLine, result.Errors.Select(TranslateError).Distinct());
                    if (string.IsNullOrWhiteSpace(ErrorMessage)) ErrorMessage = "Sprawdź poprawność danych użytkownika.";
                    break;
                case HttpStatusCode.Unauthorized:
                    ErrorMessage = "Sesja wygasła. Zaloguj się ponownie.";
                    break;
                case HttpStatusCode.Forbidden:
                    ErrorMessage = "Brak uprawnień operatora do tworzenia użytkowników.";
                    break;
                default:
                    ErrorMessage = "Nie udało się utworzyć użytkownika. Spróbuj ponownie później.";
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
            if (IsCurrentSession()) ErrorMessage = "Nie udało się utworzyć użytkownika.";
        }
        finally
        {
            if (!_isClosed) IsSaving = false;
        }
    }

    private static string TranslateError(string code) => code switch
    {
        "DuplicateUserName" => "Użytkownik o tym loginie już istnieje.",
        "DuplicateEmail" => "Podany adres e-mail jest już używany.",
        "InvalidUserName" => "Podaj poprawny login (maksymalnie 256 znaków).",
        "InvalidEmail" => "Podaj poprawny adres e-mail (maksymalnie 256 znaków).",
        "PasswordRequired" => "Podaj hasło.",
        "PasswordTooShort" => "Hasło jest zbyt krótkie.",
        "PasswordRequiresDigit" => "Hasło musi zawierać cyfrę.",
        "PasswordRequiresLower" => "Hasło musi zawierać małą literę.",
        "PasswordRequiresUpper" => "Hasło musi zawierać wielką literę.",
        "PasswordRequiresNonAlphanumeric" => "Hasło musi zawierać znak specjalny.",
        "PasswordRequiresUniqueChars" => "Hasło musi zawierać więcej różnych znaków.",
        _ => "Sprawdź poprawność danych użytkownika."
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
        Password = string.Empty;
        ConfirmPassword = string.Empty;
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
        Password = string.Empty;
        ConfirmPassword = string.Empty;
        IsSaving = false;
        IsSessionValid = false;
        _cancellation.Dispose();
    }
}
