namespace xKsef.Infrastructure.Authentication;

public sealed class BrowserSessionOptions
{
    public const string SectionName = "BrowserSessions";
    public const string AuthenticationScheme = "BrowserCookie";
    public TimeSpan IdleTimeout { get; set; } = TimeSpan.FromMinutes(30);
    public TimeSpan AbsoluteLifetime { get; set; } = TimeSpan.FromHours(8);
    internal bool IsValid() => IdleTimeout > TimeSpan.Zero && AbsoluteLifetime >= IdleTimeout;
}