public interface ITokenStore
{
    string? AccessToken { get; set; }
    long SessionVersion { get; }
    (string? AccessToken, long SessionVersion) GetSessionSnapshot();
    bool TryClearAccessToken(long expectedSessionVersion);
    bool TrySetAccessToken(long expectedSessionVersion, string accessToken);
    event EventHandler? TokenChanged;
}

public sealed class TokenChangedEventArgs : EventArgs
{
    public TokenChangedEventArgs(long sessionVersion)
    {
        SessionVersion = sessionVersion;
    }

    public long SessionVersion { get; }
}