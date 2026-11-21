using xKsef.Domain.Contractors;

namespace xKsef.Application.Contractors;

public sealed class ContractorAggregate
{
    public Contractor Contractor { get; init; } = null!;
    public ContractorsData ContractorData { get; init; } = null!;
    public IReadOnlyList<ContractorAddressAggregate> Addresses { get; init; } = [];
}
