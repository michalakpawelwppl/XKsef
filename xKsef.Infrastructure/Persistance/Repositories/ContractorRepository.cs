using Microsoft.EntityFrameworkCore;
using xKsef.Application.Contractors;
using xKsef.Domain.Contractors;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Infrastructure.Persistence.Repositories;

public sealed class ContractorRepository : IContractorRepository
{
    private readonly AppDbContext _dbContext;

    public ContractorRepository(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public Task<IReadOnlyList<ContractorAggregate>> GetContractorsAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        return ReadAsync(tenantId, null, cancellationToken);
    }

    public async Task<ContractorAggregate?> GetByIdAsync(Guid tenantId, Guid contractorId, CancellationToken cancellationToken = default)
    {
        return (await ReadAsync(tenantId, contractorId, cancellationToken)).SingleOrDefault();
    }

    private async Task<IReadOnlyList<ContractorAggregate>> ReadAsync(Guid tenantId, Guid? contractorId, CancellationToken cancellationToken)
    {
        var contractors = await (
            from contractor in _dbContext.Contractors.AsNoTracking()
            join data in _dbContext.ContractorsData.AsNoTracking() on contractor.Id equals data.ContractorId
            where contractor.TenantId == tenantId && (!contractorId.HasValue || contractor.Id == contractorId.Value)
            orderby data.FullName, contractor.Id
            select new ContractorAggregate { Contractor = contractor, ContractorData = data }
        ).ToListAsync(cancellationToken);
        if (contractors.Count == 0)
            return contractors;

        var addresses = await (
            from address in _dbContext.Addresses.AsNoTracking()
            join details in _dbContext.AddressesData.AsNoTracking() on address.Id equals details.AddressId
            join data in _dbContext.ContractorsData.AsNoTracking() on address.ContractorsDataId equals data.Id
            join contractor in _dbContext.Contractors.AsNoTracking() on data.ContractorId equals contractor.Id
            where contractor.TenantId == tenantId && (!contractorId.HasValue || contractor.Id == contractorId.Value)
            orderby address.AddressType
            select new ContractorAddressAggregate { Address = address, AddressData = details }
        ).ToListAsync(cancellationToken);
        var lookup = addresses.ToLookup(item => item.Address.ContractorsDataId);
        return contractors.Select(item => new ContractorAggregate
        {
            Contractor = item.Contractor,
            ContractorData = item.ContractorData,
            Addresses = lookup[item.ContractorData.Id].ToList()
        }).ToList();
    }

    public async Task AddAsync(ContractorAggregate aggregate, CancellationToken cancellationToken = default)
    {
        ValidateAggregate(aggregate);
        var tenantExists = await _dbContext.Tenants.AnyAsync(item => item.Id == aggregate.Contractor.TenantId && item.IsActive, cancellationToken);
        if (!tenantExists)
            throw new InvalidOperationException("Tenant nie istnieje lub jest nieaktywny.");

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        _dbContext.Contractors.Add(aggregate.Contractor);
        _dbContext.ContractorsData.Add(aggregate.ContractorData);
        foreach (var item in aggregate.Addresses)
        {
            _dbContext.Addresses.Add(item.Address);
            _dbContext.AddressesData.Add(item.AddressData);
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task UpdateAsync(Guid tenantId, ContractorAggregate aggregate, CancellationToken cancellationToken = default)
    {
        ValidateAggregate(aggregate);
        if (tenantId == Guid.Empty || aggregate.Contractor.TenantId != tenantId)
            throw new ArgumentException("Nieprawidłowy tenant kontrahenta.", nameof(tenantId));

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var contractor = await _dbContext.Contractors.SingleOrDefaultAsync(item => item.Id == aggregate.Contractor.Id && item.TenantId == tenantId, cancellationToken)
            ?? throw new KeyNotFoundException("Nie znaleziono kontrahenta dla wskazanego tenanta.");
        var data = await _dbContext.ContractorsData.SingleOrDefaultAsync(item => item.ContractorId == contractor.Id, cancellationToken)
            ?? throw new InvalidOperationException("Brak danych kontrahenta.");
        if (data.Id != aggregate.ContractorData.Id)
            throw new ArgumentException("Nieprawidłowy identyfikator danych kontrahenta.", nameof(aggregate));

        contractor.IsActive = aggregate.Contractor.IsActive;
        data.FullName = aggregate.ContractorData.FullName;
        data.ShortName = aggregate.ContractorData.ShortName;
        data.Nip = aggregate.ContractorData.Nip;
        data.Regon = aggregate.ContractorData.Regon;
        data.KrsNumber = aggregate.ContractorData.KrsNumber;
        data.EuVatNumber = aggregate.ContractorData.EuVatNumber;
        data.OtherTaxNumber = aggregate.ContractorData.OtherTaxNumber;
        data.IsEuVatRegistered = aggregate.ContractorData.IsEuVatRegistered;

        var addresses = await _dbContext.Addresses.Where(item => item.ContractorsDataId == data.Id).ToListAsync(cancellationToken);
        var details = await (
            from detail in _dbContext.AddressesData
            join address in _dbContext.Addresses on detail.AddressId equals address.Id
            where address.ContractorsDataId == data.Id
            select detail
        ).ToDictionaryAsync(item => item.AddressId, cancellationToken);
        var incoming = aggregate.Addresses.ToDictionary(item => item.Address.AddressType);
        foreach (var address in addresses)
        {
            if (!incoming.ContainsKey(address.AddressType))
            {
                if (details.TryGetValue(address.Id, out var detail))
                    _dbContext.AddressesData.Remove(detail);
                _dbContext.Addresses.Remove(address);
            }
        }
        foreach (var item in aggregate.Addresses)
        {
            var address = addresses.SingleOrDefault(existing => existing.AddressType == item.Address.AddressType);
            if (address is null)
            {
                address = new Address { Id = item.Address.Id, ContractorsDataId = data.Id, AddressType = item.Address.AddressType };
                _dbContext.Addresses.Add(address);
            }
            if (!details.TryGetValue(address.Id, out var detail))
            {
                detail = new AddressesData { Id = item.AddressData.Id, AddressId = address.Id };
                _dbContext.AddressesData.Add(detail);
            }
            detail.CountryCode = item.AddressData.CountryCode;
            detail.Street = item.AddressData.Street;
            detail.BuildingNumber = item.AddressData.BuildingNumber;
            detail.ApartmentNumber = item.AddressData.ApartmentNumber;
            detail.PostalCode = item.AddressData.PostalCode;
            detail.City = item.AddressData.City;
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static void ValidateAggregate(ContractorAggregate aggregate)
    {
        ArgumentNullException.ThrowIfNull(aggregate);
        if (aggregate.Contractor is null || aggregate.ContractorData is null || aggregate.Addresses is null)
            throw new ArgumentException("Niekompletne dane kontrahenta.", nameof(aggregate));
        if (aggregate.Contractor.Id == Guid.Empty || aggregate.Contractor.TenantId == Guid.Empty ||
            aggregate.ContractorData.Id == Guid.Empty || aggregate.ContractorData.ContractorId != aggregate.Contractor.Id)
            throw new ArgumentException("Nieprawidłowe powiązanie danych kontrahenta.", nameof(aggregate));
        if (string.IsNullOrWhiteSpace(aggregate.ContractorData.FullName) || string.IsNullOrWhiteSpace(aggregate.ContractorData.ShortName))
            throw new ArgumentException("Nazwa pełna i akronim są wymagane.", nameof(aggregate));

        var types = new HashSet<AddressType>();
        foreach (var item in aggregate.Addresses)
        {
            if (item?.Address is null || item.AddressData is null || item.Address.Id == Guid.Empty || item.AddressData.Id == Guid.Empty ||
                item.Address.ContractorsDataId != aggregate.ContractorData.Id || item.AddressData.AddressId != item.Address.Id)
                throw new ArgumentException("Nieprawidłowe powiązanie danych adresowych.", nameof(aggregate));
            if (item.Address.AddressType != AddressType.RegisteredOffice || !types.Add(item.Address.AddressType))
                throw new ArgumentException("Nieprawidłowy lub powtórzony typ adresu.", nameof(aggregate));
        }
    }
}