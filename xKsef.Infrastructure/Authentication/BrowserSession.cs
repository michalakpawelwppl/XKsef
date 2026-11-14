namespace xKsef.Infrastructure.Authentication;

public sealed class BrowserSession
{
    public required string IdHash { get; set; }
    public required string UserId { get; set; }
    public required byte[] ProtectedTicket { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset RenewedAt { get; set; }
    public DateTimeOffset IdleExpiresAt { get; set; }
    public DateTimeOffset AbsoluteExpiresAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
    public Guid Version { get; set; }
}