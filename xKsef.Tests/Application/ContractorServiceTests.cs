using xKesf.Application.Contractors;
using xKsef.Application.Contractors;
using xKsef.Domain.Contractors;

namespace xKsef.Tests.Application;

public sealed class ContractorServiceTests
{
    [Fact]
    public async Task CreateNormalizesAllIdentifiersAndCreatesAnActiveContractorWithoutAddresses()
    {
        var repository = new MemoryRepository();
        var service = new ContractorService(repository);
        var tenantId = Guid.NewGuid();
        var before = DateTimeOffset.UtcNow;
        var id = await service.CreateAsync(tenantId, new CreateContractorCommand
        {
            FullName = " Pełna nazwa ", ShortName = " Akronim ", Nip = " 8567346215 ", Regon = " 012345675 ",
            KrsNumber = " 0000123456 ", EuVatNumber = " PL8567346215 ", OtherTaxNumber = " OTHER-1 ", IsEuVatRegistered = true
        });
        var saved = repository.Saved!;

        Assert.Equal(id, saved.Contractor.Id);
        Assert.Equal(tenantId, saved.Contractor.TenantId);
        Assert.True(saved.Contractor.IsActive);
        Assert.InRange(saved.Contractor.CreatedAt, before, DateTimeOffset.UtcNow);
        Assert.Equal("Pełna nazwa", saved.ContractorData.FullName);
        Assert.Equal("Akronim", saved.ContractorData.ShortName);
        Assert.Equal("8567346215", saved.ContractorData.Nip);
        Assert.Equal("012345675", saved.ContractorData.Regon);
        Assert.Equal("0000123456", saved.ContractorData.KrsNumber);
        Assert.Equal("PL8567346215", saved.ContractorData.EuVatNumber);
        Assert.Equal("OTHER-1", saved.ContractorData.OtherTaxNumber);
        Assert.True(saved.ContractorData.IsEuVatRegistered);
        Assert.Equal(id, saved.ContractorData.ContractorId);
        Assert.Empty(saved.Addresses);
    }

    [Fact]
    public async Task UpdatePreservesAllIdsAndCreationDateWhileDeactivatingAndReactivating()
    {
        var repository = new MemoryRepository();
        var service = new ContractorService(repository);
        var tenantId = Guid.NewGuid();
        var id = await service.CreateAsync(tenantId, ValidCreate([Address(" Poznań ")]));
        var original = repository.Saved!;
        var originalAddress = Assert.Single(original.Addresses);
        Assert.Equal("PL", originalAddress.AddressData.CountryCode);
        Assert.Equal("Poznań", originalAddress.AddressData.City);
        Assert.Equal(originalAddress.Address.Id, originalAddress.AddressData.AddressId);
        Assert.Equal(original.ContractorData.Id, originalAddress.Address.ContractorsDataId);
        foreach (var active in new[] { false, true })
        {
            await service.UpdateAsync(tenantId, id, new UpdateContractorCommand
            {
                FullName = "Zmieniona", ShortName = "ZMIANA", IsActive = active, Addresses = [Address("Warszawa")]
            });
            var saved = repository.Saved!;
            Assert.Equal(original.Contractor.Id, saved.Contractor.Id);
            Assert.Equal(original.Contractor.CreatedAt, saved.Contractor.CreatedAt);
            Assert.Equal(original.ContractorData.Id, saved.ContractorData.Id);
            Assert.Equal(active, saved.Contractor.IsActive);
            Assert.Equal(originalAddress.Address.Id, Assert.Single(saved.Addresses).Address.Id);
            Assert.Equal(originalAddress.AddressData.Id, saved.Addresses[0].AddressData.Id);
            Assert.Equal("Warszawa", saved.Addresses[0].AddressData.City);
        }
    }

    [Theory]
    [InlineData("short-empty")]
    [InlineData("short-long")]
    [InlineData("full-empty")]
    [InlineData("full-long")]
    [InlineData("nip")]
    [InlineData("regon")]
    [InlineData("krs")]
    [InlineData("vat-missing")]
    [InlineData("vat-forbidden")]
    [InlineData("vat-long")]
    [InlineData("other-long")]
    public async Task InvalidDataIsRejectedBeforeWriting(string invalid)
    {
        var repository = new MemoryRepository();
        var service = new ContractorService(repository);
        var command = new CreateContractorCommand
        {
            FullName = invalid == "full-empty" ? " " : invalid == "full-long" ? new string('x', 501) : "Firma",
            ShortName = invalid == "short-empty" ? " " : invalid == "short-long" ? new string('x', 101) : "F",
            Nip = invalid == "nip" ? "8567346216" : null,
            Regon = invalid == "regon" ? "123456786" : null,
            KrsNumber = invalid == "krs" ? "123" : null,
            IsEuVatRegistered = invalid is "vat-missing" or "vat-long",
            EuVatNumber = invalid == "vat-forbidden" ? "PL8567346215" : invalid == "vat-long" ? new string('x', 33) : null,
            OtherTaxNumber = invalid == "other-long" ? new string('x', 101) : null
        };
        await Assert.ThrowsAsync<ArgumentException>(() => service.CreateAsync(Guid.NewGuid(), command));
        Assert.Null(repository.Saved);
    }

    [Theory]
    [InlineData("duplicate")]
    [InlineData("undefined")]
    [InlineData("zero")]
    [InlineData("null-item")]
    [InlineData("null-list")]
    public async Task InvalidAddressCollectionsAreRejected(string invalid)
    {
        var service = new ContractorService(new MemoryRepository());
        IReadOnlyList<CreateContractorAddress> addresses = invalid switch
        {
            "duplicate" => [Address("A"), Address("B")],
            "undefined" => [new CreateContractorAddress { AddressType = (AddressType)2, City = "A" }],
            "zero" => [new CreateContractorAddress { City = "A" }],
            "null-item" => [null!],
            _ => null!
        };
        await Assert.ThrowsAnyAsync<ArgumentException>(() => service.CreateAsync(Guid.NewGuid(), ValidCreate(addresses)));
    }

    [Fact]
    public async Task ForeignTenantCannotReadOrUpdateContractor()
    {
        var repository = new MemoryRepository();
        var service = new ContractorService(repository);
        var id = await service.CreateAsync(Guid.NewGuid(), ValidCreate([]));
        var foreignTenant = Guid.NewGuid();
        Assert.Null(await service.GetByIdAsync(foreignTenant, id));
        Assert.Empty(await service.GetContractorsAsync(foreignTenant));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => service.UpdateAsync(foreignTenant, id, new UpdateContractorCommand { FullName = "X", ShortName = "X", IsActive = false }));
        Assert.True(repository.Saved!.Contractor.IsActive);
    }

    private static CreateContractorCommand ValidCreate(IReadOnlyList<CreateContractorAddress> addresses) => new() { FullName = "Firma", ShortName = "F", Addresses = addresses };
    private static CreateContractorAddress Address(string city) => new() { AddressType = AddressType.RegisteredOffice, CountryCode = " pl ", City = city };

    private sealed class MemoryRepository : IContractorRepository
    {
        public ContractorAggregate? Saved { get; private set; }
        public Task AddAsync(ContractorAggregate aggregate, CancellationToken cancellationToken = default) { Saved = aggregate; return Task.CompletedTask; }
        public Task UpdateAsync(Guid tenantId, ContractorAggregate aggregate, CancellationToken cancellationToken = default) { Saved = aggregate; return Task.CompletedTask; }
        public Task<ContractorAggregate?> GetByIdAsync(Guid tenantId, Guid contractorId, CancellationToken cancellationToken = default) => Task.FromResult(Saved?.Contractor.TenantId == tenantId && Saved.Contractor.Id == contractorId ? Saved : null);
        public Task<IReadOnlyList<ContractorAggregate>> GetContractorsAsync(Guid tenantId, CancellationToken cancellationToken = default) => Task.FromResult<IReadOnlyList<ContractorAggregate>>(Saved?.Contractor.TenantId == tenantId ? [Saved!] : []);
    }
}
