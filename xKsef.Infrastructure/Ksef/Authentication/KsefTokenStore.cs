namespace xKsef.Infrastructure.Ksef.Authentication;

public sealed class KsefTokenStore
{
    private readonly object _sync = new();

    private KsefTokens? _tokens;

    public void Set(KsefTokens tokens)
    {
        lock (_sync)
        {
            _tokens = tokens;
        }
    }

    public KsefTokens? Get()
    {
        lock (_sync)
        {
            return _tokens;
        }
    }

    public void Clear()
    {
        lock (_sync)
        {
            _tokens = null;
        }
    }
}