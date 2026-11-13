namespace xKsef.Infrastructure.Ksef.Authentication;

public sealed class PendingKsefAuthentication
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public required string UserId { get; init; }
    public required Guid TenantId { get; init; }
    public required string Nip { get; init; }
    public required string Challenge { get; init; }
    public DateTimeOffset CreatedAt { get; init; } = DateTimeOffset.UtcNow;
}