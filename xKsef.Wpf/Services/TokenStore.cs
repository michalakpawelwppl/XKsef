using Azure.Core;

public class TokenStore : ITokenStore
{
    private string? _accessToken;
    public event EventHandler? TokenChanged;

    public string? AccessToken
    {
        get => _accessToken;
        set
        {
            if (_accessToken == value)
                return;

            _accessToken = value;
            TokenChanged?.Invoke(this, EventArgs.Empty);
        }
    }
}