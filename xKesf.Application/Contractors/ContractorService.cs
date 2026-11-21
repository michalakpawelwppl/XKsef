using xKesf.Application.Contractors;
using xKsef.Domain.Contractors;

namespace xKsef.Application.Contractors;

public sealed class ContractorService : IContractorService
{
    private readonly IContractorRepository _repository;

    public ContractorService(IContractorRepository repository)
    {
        _repository = repository;
    }

    public Task<IReadOnlyList<ContractorAggregate>> GetContractorsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return _repository.GetContractorsAsync(tenantId, cancellationToken);
    }

    public Task<ContractorAggregate?> GetByIdAsync(Guid tenantId, Guid contractorId, CancellationToken cancellationToken = default)
    {
        return _repository.GetByIdAsync(tenantId, contractorId, cancellationToken);
    }

    public async Task<Guid> CreateAsync(Guid tenantId, CreateContractorCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Nieprawidłowy identyfikator tenanta.", nameof(tenantId));

        var data = CreateData(command);
        var contractor = new Contractor { Id = Guid.NewGuid(), TenantId = tenantId, IsActive = true, CreatedAt = DateTimeOffset.UtcNow };
        data.Id = Guid.NewGuid();
        data.ContractorId = contractor.Id;
        var addresses = CreateAddresses(data.Id, command.Addresses, []);
        await _repository.AddAsync(new ContractorAggregate { Contractor = contractor, ContractorData = data, Addresses = addresses }, cancellationToken);
        return contractor.Id;
    }

    public async Task UpdateAsync(Guid tenantId, Guid contractorId, UpdateContractorCommand command, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (tenantId == Guid.Empty || contractorId == Guid.Empty)
            throw new ArgumentException("Nieprawidłowy identyfikator tenanta lub kontrahenta.");

        var data = CreateData(new CreateContractorCommand
        {
            FullName = command.FullName,
            ShortName = command.ShortName,
            Nip = command.Nip,
            Regon = command.Regon,
            KrsNumber = command.KrsNumber,
            EuVatNumber = command.EuVatNumber,
            OtherTaxNumber = command.OtherTaxNumber,
            IsEuVatRegistered = command.IsEuVatRegistered
        });
        var existing = await _repository.GetByIdAsync(tenantId, contractorId, cancellationToken)
            ?? throw new KeyNotFoundException("Nie znaleziono kontrahenta dla wskazanego tenanta.");
        data.Id = existing.ContractorData.Id;
        data.ContractorId = contractorId;
        var addresses = CreateAddresses(data.Id, command.Addresses, existing.Addresses);
        await _repository.UpdateAsync(tenantId, new ContractorAggregate
        {
            Contractor = new Contractor { Id = existing.Contractor.Id, TenantId = existing.Contractor.TenantId, CreatedAt = existing.Contractor.CreatedAt, IsActive = command.IsActive },
            ContractorData = data,
            Addresses = addresses
        }, cancellationToken);
    }

    private static ContractorsData CreateData(CreateContractorCommand command)
    {
        return new ContractorsData
        {
            FullName = RequiredText(command.FullName, 500, "Nazwa pełna"),
            ShortName = RequiredText(command.ShortName, 100, "Akronim"),
            Nip = ContractorIdentifierValidator.NormalizeAndValidateNip(command.Nip),
            Regon = ContractorIdentifierValidator.NormalizeAndValidateRegon(command.Regon),
            KrsNumber = ContractorIdentifierValidator.NormalizeAndValidateKrsNumber(command.KrsNumber),
            EuVatNumber = ContractorIdentifierValidator.NormalizeAndValidateEuVatNumber(command.EuVatNumber, command.IsEuVatRegistered),
            OtherTaxNumber = ContractorIdentifierValidator.NormalizeAndValidateOtherTaxNumber(command.OtherTaxNumber),
            IsEuVatRegistered = command.IsEuVatRegistered
        };
    }

    private static IReadOnlyList<ContractorAddressAggregate> CreateAddresses(Guid dataId, IReadOnlyList<CreateContractorAddress> addresses, IReadOnlyList<ContractorAddressAggregate> existing)
    {
        ArgumentNullException.ThrowIfNull(addresses);
        var types = new HashSet<AddressType>();
        var result = new List<ContractorAddressAggregate>();
        foreach (var address in addresses)
        {
            if (address is null || address.AddressType != AddressType.RegisteredOffice)
                throw new ArgumentException("Nieprawidłowy typ adresu.", nameof(addresses));
            if (!types.Add(address.AddressType))
                throw new ArgumentException("Typ adresu nie może się powtarzać.", nameof(addresses));

            var country = RequiredText(address.CountryCode, 2, "Kod kraju").ToUpperInvariant();
            if (country.Length != 2)
                throw new ArgumentException("Kod kraju musi zawierać 2 znaki.");
            var previous = existing.SingleOrDefault(item => item.Address.AddressType == address.AddressType);
            var id = previous?.Address.Id ?? Guid.NewGuid();
            result.Add(new ContractorAddressAggregate
            {
                Address = new Address { Id = id, ContractorsDataId = dataId, AddressType = address.AddressType },
                AddressData = new AddressesData
                {
                    Id = previous?.AddressData.Id ?? Guid.NewGuid(),
                    AddressId = id,
                    CountryCode = country,
                    Street = OptionalAddressText(address.Street, 200) ?? string.Empty,
                    BuildingNumber = OptionalAddressText(address.BuildingNumber, 20) ?? string.Empty,
                    ApartmentNumber = OptionalAddressText(address.ApartmentNumber, 20),
                    PostalCode = OptionalAddressText(address.PostalCode, 20) ?? string.Empty,
                    City = RequiredText(address.City, 200, "Miejscowość")
                }
            });
        }
        return result;
    }

    private static string RequiredText(string? value, int maximum, string label)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrEmpty(normalized) || normalized.Length > maximum)
            throw new ArgumentException($"{label} musi zawierać od 1 do {maximum} znaków.");
        return normalized;
    }

    private static string? OptionalAddressText(string? value, int maximum)
    {
        var normalized = ContractorIdentifierNormalizer.NormalizeOptional(value);
        if (normalized?.Length > maximum)
            throw new ArgumentException($"Pole adresu nie może przekraczać {maximum} znaków.");
        return normalized;
    }
}