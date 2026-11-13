using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Net.Http;
using xKsef.Contracts.Authentication;
using xKsef.Wpf.Authentication;
using xKsef.Wpf.Http;

namespace xKsef.Wpf.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    private readonly record struct LoginAttempt(long Id, long SessionVersion, ApiRequestContext Context);

    public event EventHandler? LoginSucceeded;

    private readonly IAuthenticationApiClient _authenticationApiClient;
    private readonly ITokenStore _tokenStore;
    private readonly IWindowNavigationService _navigationService;
    private readonly object _loginAttemptSync = new();
    private long _loginAttemptId;


    [ObservableProperty]
    private string _login = string.Empty;
    
    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public LoginViewModel(IAuthenticationApiClient authenticationApiClient, ITokenStore tokenStore, IWindowNavigationService navigationService)
    {
        _authenticationApiClient = authenticationApiClient;
        _tokenStore = tokenStore;
        _navigationService = navigationService;
    }

    public async Task<bool> LoginAsync(string password, CancellationToken cancellationToken = default)
    {
        LoginAttempt attempt = BeginLoginAttempt(cancellationToken);
        LoginResponse? response = await _authenticationApiClient.LoginAsync(attempt.Context, Login, password);
        return response is not null && TryCommitLogin(attempt, response.AccessToken);
    }

    [RelayCommand]
    private async Task ExecuteLoginAsync(string? password)
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Login) || string.IsNullOrEmpty(password))
        {
            ErrorMessage = "Podaj login i hasło.";
            return;
        }

        LoginAttempt? attempt = null;
        try
        {
            LoginAttempt currentAttempt = BeginLoginAttempt(CancellationToken.None);
            attempt = currentAttempt;
            LoginResponse? response = await _authenticationApiClient.LoginAsync(currentAttempt.Context, Login, password);

            if (response is null)
            {
                TrySetErrorIfCurrent(currentAttempt, "Nieprawidłowy login lub hasło.");
                return;
            }

            TryCommitLogin(currentAttempt, response.AccessToken, () => LoginSucceeded?.Invoke(this, EventArgs.Empty));
        }
        catch (HttpRequestException)
        {
            if (attempt is LoginAttempt currentAttempt)
                TrySetErrorIfCurrent(currentAttempt, "Nie można połączyć się z API.");
        }
        catch (Exception)
        {
            if (attempt is LoginAttempt currentAttempt)
                TrySetErrorIfCurrent(currentAttempt, "Wystąpił błąd podczas logowania.");
        }
    }

    private LoginAttempt BeginLoginAttempt(CancellationToken cancellationToken)
    {
        lock (_loginAttemptSync)
        {
            long attemptId = ++_loginAttemptId;
            _tokenStore.AccessToken = null;
            var session = _tokenStore.GetSessionSnapshot();
            var context = new ApiRequestContext(
                attemptId,
                session.SessionVersion,
                TenantId: null,
                AccessToken: null,
                cancellationToken,
                ApiRequestScope.Anonymous);

            return new LoginAttempt(attemptId, session.SessionVersion, context);
        }
    }

    private bool TryCommitLogin(LoginAttempt attempt, string accessToken, Action? onCommitted = null)
    {
        lock (_loginAttemptSync)
        {
            if (_loginAttemptId != attempt.Id || !_tokenStore.TrySetAccessToken(attempt.SessionVersion, accessToken))
                return false;

            onCommitted?.Invoke();
            return true;
        }
    }

    private void TrySetErrorIfCurrent(LoginAttempt attempt, string message)
    {
        lock (_loginAttemptSync)
        {
            if (_loginAttemptId == attempt.Id && _tokenStore.SessionVersion == attempt.SessionVersion)
                ErrorMessage = message;
        }
    }
}