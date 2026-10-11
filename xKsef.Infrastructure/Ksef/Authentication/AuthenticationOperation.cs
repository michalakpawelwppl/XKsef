namespace xKsef.Infrastructure.Ksef.Authentication;

public sealed class AuthenticationOperation
{
    public required string ReferenceNumber { get; init; }
    public required string AuthenticationToken { get; init; }
}