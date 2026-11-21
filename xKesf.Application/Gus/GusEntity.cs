namespace xKsef.Application.Gus;

public sealed record GusEntity
{
    public required string Regon { get; init; }
    public string? Nip { get; init; }
    // The basic BIR search response does not supply KRS; do not infer it from the search input.
    public string? KrsNumber { get; init; }
    public required string FullName { get; init; }
    public string? NipStatus { get; init; }
    public string? EntityType { get; init; }
    public int? SiloId { get; init; }
    public GusAddress Address { get; init; } = new();
}

public sealed record GusAddress
{
    public string? Voivodeship { get; init; }
    public string? County { get; init; }
    public string? Municipality { get; init; }
    public string? City { get; init; }
    public string? PostalCode { get; init; }
    public string? Street { get; init; }
    public string? BuildingNumber { get; init; }
    public string? ApartmentNumber { get; init; }
    public string? PostOfficeCity { get; init; }
}
