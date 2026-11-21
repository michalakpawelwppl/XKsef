namespace xKesf.Application.Contractors
{
    public sealed class CreateContractorCommand
    {
        public string FullName { get; init; } = string.Empty;
        public string ShortName { get; init; } = string.Empty;
        public string? Nip { get; init; }
        public string? Regon { get; init; }
        public string? KrsNumber { get; init; }
        public string? EuVatNumber { get; init; }
        public string? OtherTaxNumber { get; init; }
        public bool IsEuVatRegistered { get; init; }
        public IReadOnlyList<CreateContractorAddress> Addresses { get; init; } = [];
    }
    public sealed class CreateContractorAddress
    {
        public xKsef.Domain.Contractors.AddressType AddressType { get; init; }
        public string CountryCode { get; init; } = "PL";
        public string Street { get; init; } = string.Empty;
        public string BuildingNumber { get; init; } = string.Empty;
        public string? ApartmentNumber { get; init; }
        public string PostalCode { get; init; } = string.Empty;
        public string City { get; init; } = string.Empty;
    }
}
