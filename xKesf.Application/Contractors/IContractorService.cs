using xKesf.Application.Contractors;

namespace xKsef.Application.Contractors;

public interface IContractorService
{
    Task<IReadOnlyList<ContractorAggregate>> GetContractorsAsync(Guid tenantId, CancellationToken cancellationToken = default);

    Task<ContractorAggregate?> GetByIdAsync(Guid tenantId, Guid contractorId, CancellationToken cancellationToken = default);
    Task<Guid> CreateAsync(Guid tenantId, CreateContractorCommand command, CancellationToken cancellationToken = default);
    Task UpdateAsync(Guid tenantId, Guid contractorId, UpdateContractorCommand command, CancellationToken cancellationToken = default);

}
