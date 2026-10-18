using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Net.Http;
using System.Windows.Controls;
using xKsef.Wpf.Authentication;

namespace xKsef.Wpf.ViewModels;

public sealed partial class LoginViewModel : ObservableObject
{
    public event EventHandler? LoginSucceeded;

    private readonly IAuthenticationApiClient _authenticationApiClient;
    private readonly ITokenStore _tokenStore;
    private readonly IWindowNavigationService _navigationService;


    [ObservableProperty]
    private string _login = string.Empty;
    
    [ObservableProperty]
    private string password = string.Empty;

    [ObservableProperty]
    private string _errorMessage = string.Empty;

    public LoginViewModel(
        IAuthenticationApiClient authenticationApiClient, ITokenStore tokenStore, IWindowNavigationService navigationService)
    {
        _authenticationApiClient = authenticationApiClient;
        _tokenStore = tokenStore;
        _navigationService = navigationService;
    }

    public async Task<bool> LoginAsync(
    string password,
    CancellationToken cancellationToken = default)
    {
        _tokenStore.AccessToken = null;
        var response = await _authenticationApiClient.LoginAsync(
            Login,
            password,
            cancellationToken);
        if (response!= null)
        {
        _tokenStore.AccessToken = response.AccessToken;

        }

        return response is not null;
    }

    [RelayCommand]
    private async Task ExecuteLoginAsync(string? password)
    {
        ErrorMessage = string.Empty;

        if (string.IsNullOrWhiteSpace(Login) ||
            string.IsNullOrEmpty(password))
        {
            ErrorMessage = "Podaj login i hasło.";
            return;
        }

        try
        {
            bool success = await LoginAsync(password);

            if (!success)
            {
                ErrorMessage = "Nieprawidłowy login lub hasło.";
                return;
            }
            LoginSucceeded?.Invoke(this, EventArgs.Empty);
            // Logowanie zakończone sukcesem.
            // W kolejnych krokach obsłużymy token JWT
            // i przejście do głównego okna.
        }
        catch (HttpRequestException)
        {
            ErrorMessage = "Nie można połączyć się z API.";
        }
        catch (Exception)
        {
            ErrorMessage = "Wystąpił błąd podczas logowania.";
        }
    }
}