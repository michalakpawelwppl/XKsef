namespace xKsef.Application.Contractors;

public interface IContractorRepository
{
    Task<IReadOnlyList<ContractorAggregate>> GetContractorsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<ContractorAggregate?> GetByIdAsync(Guid tenantId, Guid contractorId, CancellationToken cancellationToken = default);

    Task AddAsync(ContractorAggregate aggregate, CancellationToken cancellationToken = default);

    Task UpdateAsync(Guid tenantId, ContractorAggregate aggregate, CancellationToken cancellationToken = default);


}
