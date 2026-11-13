public class TokenStore : ITokenStore
{
    private readonly object _sync = new();
    private string? _accessToken;
    private long _sessionVersion;
    public event EventHandler? TokenChanged;

    public string? AccessToken
    {
        get
        {
            lock (_sync)
                return _accessToken;
        }
        set
        {
            long sessionVersion;

            lock (_sync)
            {
                if (_accessToken == value)
                    return;

                _accessToken = value;
                sessionVersion = ++_sessionVersion;
            }

            TokenChanged?.Invoke(this, new TokenChangedEventArgs(sessionVersion));
        }
    }

    public long SessionVersion
    {
        get
        {
            lock (_sync)
                return _sessionVersion;
        }
    }

    public (string? AccessToken, long SessionVersion) GetSessionSnapshot()
    {
        lock (_sync)
            return (_accessToken, _sessionVersion);
    }

    public bool TryClearAccessToken(long expectedSessionVersion)
    {
        long sessionVersion;

        lock (_sync)
        {
            if (_sessionVersion != expectedSessionVersion || _accessToken is null)
                return false;

            _accessToken = null;
            sessionVersion = ++_sessionVersion;
        }

        TokenChanged?.Invoke(this, new TokenChangedEventArgs(sessionVersion));
        return true;
    }

    public bool TrySetAccessToken(long expectedSessionVersion, string accessToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(accessToken);

        long sessionVersion;
        lock (_sync)
        {
            if (_sessionVersion != expectedSessionVersion)
                return false;

            if (_accessToken == accessToken)
                return true;

            _accessToken = accessToken;
            sessionVersion = ++_sessionVersion;
        }

        TokenChanged?.Invoke(this, new TokenChangedEventArgs(sessionVersion));
        return true;
    }
}