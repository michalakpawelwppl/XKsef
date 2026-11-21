namespace xKsef.Contracts.Contractors;

public sealed class AddressDto
{
    public int AddressType { get; set; }
    public string CountryCode { get; set; } = "PL";
    public string Street { get; set; } = string.Empty;
    public string BuildingNumber { get; set; } = string.Empty;
    public string? ApartmentNumber { get; set; }
    public string PostalCode { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
}
