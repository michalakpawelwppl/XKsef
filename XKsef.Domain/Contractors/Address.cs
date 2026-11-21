public sealed class Address
{
    public Guid Id { get; set; }
    public Guid ContractorsDataId { get; set; }
    public xKsef.Domain.Contractors.AddressType AddressType { get; set; }
}
