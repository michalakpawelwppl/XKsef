namespace xKsef.Infrastructure.Authentication;

public sealed class BrowserSessionIdentityChangedException : InvalidOperationException
{
    public BrowserSessionIdentityChangedException() : base("User security state changed during sign-in.") { }
}
