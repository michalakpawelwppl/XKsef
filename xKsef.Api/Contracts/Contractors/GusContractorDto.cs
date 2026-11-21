using xKsef.Application.Gus;

namespace xKsef.Api.Contracts.Contractors;

public sealed record GusContractorDto
{
    public string? Nip { get; init; }
    public required string Regon { get; init; }
    public string? Krs { get; init; }
    public required string FullName { get; init; }
    public string? ShortName => null;
    public required GusAddress Address { get; init; }
}
