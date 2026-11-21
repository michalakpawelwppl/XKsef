using System.Data.Common;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using xKesf.Application.Contractors;
using xKsef.Application.Contractors;
using xKsef.Domain.Contractors;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Persistence;
using xKsef.Infrastructure.Persistence.Repositories;

namespace xKsef.Tests.Infrastructure;

public sealed class ContractorRepositoryTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateAndReadRoundTripWithOrWithoutRegisteredOffice(bool withAddress)
    {
        await using var store = await ContractorTestStore.CreateAsync();
        var id = await store.Service.CreateAsync(store.TenantA, ContractorTestStore.Command(withAddress));
        store.Context.ChangeTracker.Clear();
        var read = (await store.Repository.GetByIdAsync(store.TenantA, id))!;
        Assert.Equal(id, read.Contractor.Id);
        Assert.Equal("Firma", read.ContractorData.FullName);
        Assert.Equal("F", read.ContractorData.ShortName);
        Assert.Equal("8567346215", read.ContractorData.Nip);
        Assert.Equal(withAddress ? 1 : 0, read.Addresses.Count);
        if (withAddress)
        {
            var address = Assert.Single(read.Addresses);
            Assert.Equal(AddressType.RegisteredOffice, address.Address.AddressType);
            Assert.Equal("Poznań", address.AddressData.City);
            Assert.Equal(address.Address.Id, address.AddressData.AddressId);
        }
        Assert.Empty(store.Context.ChangeTracker.Entries());
    }

    [Fact]
    public async Task UpdatePreservesIdsAndCreationDateAndAllowsDeactivationReactivationAndRemovingAddress()
    {
        await using var store = await ContractorTestStore.CreateAsync();
        var id = await store.Service.CreateAsync(store.TenantA, ContractorTestStore.Command(true));
        var original = (await store.Repository.GetByIdAsync(store.TenantA, id))!;
        var address = Assert.Single(original.Addresses);
        foreach (var active in new[] { false, true })
        {
            await store.Service.UpdateAsync(store.TenantA, id, new UpdateContractorCommand
            {
                FullName = "Zmieniona", ShortName = "Z", IsActive = active,
                Regon = "012345675", KrsNumber = "0000123456", EuVatNumber = "PL8567346215", IsEuVatRegistered = true, OtherTaxNumber = "OTHER",
                Addresses = [new CreateContractorAddress { AddressType = AddressType.RegisteredOffice, City = "Warszawa" }]
            });
            store.Context.ChangeTracker.Clear();
            var read = (await store.Repository.GetByIdAsync(store.TenantA, id))!;
            Assert.Equal(original.Contractor.Id, read.Contractor.Id);
            Assert.Equal(original.Contractor.CreatedAt, read.Contractor.CreatedAt);
            Assert.Equal(original.ContractorData.Id, read.ContractorData.Id);
            Assert.Equal(active, read.Contractor.IsActive);
            Assert.Equal(address.Address.Id, Assert.Single(read.Addresses).Address.Id);
            Assert.Equal(address.AddressData.Id, read.Addresses[0].AddressData.Id);
            Assert.Equal("Warszawa", read.Addresses[0].AddressData.City);
            Assert.Equal("012345675", read.ContractorData.Regon);
            Assert.Equal("0000123456", read.ContractorData.KrsNumber);
            Assert.Equal("OTHER", read.ContractorData.OtherTaxNumber);
            Assert.True(read.ContractorData.IsEuVatRegistered);
            Assert.Equal("PL8567346215", read.ContractorData.EuVatNumber);
            Assert.Single(await store.Repository.GetContractorsAsync(store.TenantA));
        }
        await store.Service.UpdateAsync(store.TenantA, id, new UpdateContractorCommand { FullName = "Bez adresu", ShortName = "B", IsActive = true, Addresses = [] });
        store.Context.ChangeTracker.Clear();
        Assert.Empty((await store.Repository.GetByIdAsync(store.TenantA, id))!.Addresses);
        Assert.Empty(await store.Context.Addresses.ToListAsync());
        Assert.Empty(await store.Context.AddressesData.ToListAsync());
        await store.Service.UpdateAsync(store.TenantA, id, new UpdateContractorCommand
        {
            FullName = "Nowy adres", ShortName = "N", IsActive = true,
            Addresses = [new CreateContractorAddress { AddressType = AddressType.RegisteredOffice, City = "Gdańsk" }]
        });
        Assert.NotEqual(address.Address.Id, Assert.Single((await store.Repository.GetByIdAsync(store.TenantA, id))!.Addresses).Address.Id);
    }

    [Fact]
    public async Task ListUsesTwoQueriesRegardlessOfContractorCountAndSeparatesTenants()
    {
        await using var store = await ContractorTestStore.CreateAsync();
        for (var i = 0; i < 5; i++)
            await store.Service.CreateAsync(store.TenantA, ContractorTestStore.Command(i % 2 == 0));
        var foreignId = await store.Service.CreateAsync(store.TenantB, ContractorTestStore.Command(true));
        store.Context.ChangeTracker.Clear();
        store.Sql.Reads = 0;
        var read = await store.Repository.GetContractorsAsync(store.TenantA);
        Assert.Equal(2, store.Sql.Reads);
        Assert.Equal(5, read.Count);
        Assert.Equal(5, read.Select(item => item.Contractor.Id).Distinct().Count());
        Assert.Equal(3, read.Sum(item => item.Addresses.Count));
        Assert.All(read, item => Assert.Equal(store.TenantA, item.Contractor.TenantId));
        Assert.Null(await store.Repository.GetByIdAsync(store.TenantA, foreignId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.Service.UpdateAsync(store.TenantA, foreignId, new UpdateContractorCommand { FullName = "Atak", ShortName = "A", IsActive = false }));
        Assert.True((await store.Repository.GetByIdAsync(store.TenantB, foreignId))!.Contractor.IsActive);
    }

    [Fact]
    public async Task RepositoryChecksTenantEvenWhenCalledWithoutService()
    {
        await using var store = await ContractorTestStore.CreateAsync();
        var id = await store.Service.CreateAsync(store.TenantB, ContractorTestStore.Command(true));
        var foreign = (await store.Repository.GetByIdAsync(store.TenantB, id))!;
        foreign.Contractor.TenantId = store.TenantA;
        await Assert.ThrowsAsync<KeyNotFoundException>(() => store.Repository.UpdateAsync(store.TenantA, foreign));
        Assert.Equal(store.TenantB, (await store.Repository.GetByIdAsync(store.TenantB, id))!.Contractor.TenantId);
    }

    [Fact]
    public async Task RepositoryMatchesExistingAddressByTypeRatherThanReplacingItsIds()
    {
        await using var store = await ContractorTestStore.CreateAsync();
        var id = await store.Service.CreateAsync(store.TenantA, ContractorTestStore.Command(true));
        var source = (await store.Repository.GetByIdAsync(store.TenantA, id))!;
        var existing = Assert.Single(source.Addresses);
        var replacement = new Address { Id = Guid.NewGuid(), ContractorsDataId = source.ContractorData.Id, AddressType = AddressType.RegisteredOffice };
        await store.Repository.UpdateAsync(store.TenantA, new ContractorAggregate
        {
            Contractor = source.Contractor, ContractorData = source.ContractorData,
            Addresses = [new ContractorAddressAggregate
            {
                Address = replacement,
                AddressData = new AddressesData { Id = Guid.NewGuid(), AddressId = replacement.Id, City = "Wrocław" }
            }]
        });
        store.Context.ChangeTracker.Clear();
        var updated = Assert.Single((await store.Repository.GetByIdAsync(store.TenantA, id))!.Addresses);
        Assert.Equal(existing.Address.Id, updated.Address.Id);
        Assert.Equal(existing.AddressData.Id, updated.AddressData.Id);
        Assert.Equal("Wrocław", updated.AddressData.City);
    }

    [Fact]
    public async Task FailedUpdateRollsBackContractorAndAddressChanges()
    {
        await using var store = await ContractorTestStore.CreateAsync();
        var id = await store.Service.CreateAsync(store.TenantA, ContractorTestStore.Command(true));
        var original = (await store.Repository.GetByIdAsync(store.TenantA, id))!;
        original.Contractor.IsActive = false;
        original.ContractorData.FullName = "Nie zapisywać";
        original.Addresses[0].AddressData.City = null!;
        await Assert.ThrowsAsync<DbUpdateException>(() => store.Repository.UpdateAsync(store.TenantA, original));
        store.Context.ChangeTracker.Clear();
        var read = (await store.Repository.GetByIdAsync(store.TenantA, id))!;
        Assert.True(read.Contractor.IsActive);
        Assert.Equal("Firma", read.ContractorData.FullName);
        Assert.Equal("Poznań", Assert.Single(read.Addresses).AddressData.City);
    }

    [Fact]
    public async Task FailedInsertDoesNotLeavePartialContractor()
    {
        await using var store = await ContractorTestStore.CreateAsync();
        var id = await store.Service.CreateAsync(store.TenantA, ContractorTestStore.Command(true));
        var contractor = new Contractor { Id = Guid.NewGuid(), TenantId = store.TenantA, CreatedAt = DateTimeOffset.UtcNow };
        var data = new ContractorsData { Id = Guid.NewGuid(), ContractorId = contractor.Id, FullName = "Nowy", ShortName = "N" };
        var address = new Address { Id = Guid.NewGuid(), ContractorsDataId = data.Id, AddressType = AddressType.RegisteredOffice };
        var aggregate = new ContractorAggregate
        {
            Contractor = contractor, ContractorData = data,
            Addresses = [new ContractorAddressAggregate { Address = address, AddressData = new AddressesData { Id = Guid.NewGuid(), AddressId = address.Id, City = null! } }]
        };
        await Assert.ThrowsAsync<DbUpdateException>(() => store.Repository.AddAsync(aggregate));
        store.Context.ChangeTracker.Clear();
        Assert.Null(await store.Repository.GetByIdAsync(store.TenantA, contractor.Id));
        Assert.Single(await store.Context.Contractors.ToListAsync());
        Assert.Single(await store.Context.ContractorsData.ToListAsync());
        Assert.Single(await store.Context.Addresses.ToListAsync());
        Assert.Single(await store.Context.AddressesData.ToListAsync());
    }

    [Theory]
    [InlineData("duplicate-type")]
    [InlineData("undefined-type")]
    [InlineData("vat-missing")]
    [InlineData("vat-forbidden")]
    public async Task RelationalConstraintsRejectInvalidDataEvenWithoutService(string invalid)
    {
        await using var store = await ContractorTestStore.CreateAsync();
        var id = await store.Service.CreateAsync(store.TenantA, ContractorTestStore.Command(true));
        var source = (await store.Repository.GetByIdAsync(store.TenantA, id))!;
        if (invalid == "duplicate-type")
            store.Context.Addresses.Add(new Address { Id = Guid.NewGuid(), ContractorsDataId = source.ContractorData.Id, AddressType = AddressType.RegisteredOffice });
        else if (invalid == "undefined-type")
            (await store.Context.Addresses.SingleAsync()).AddressType = (AddressType)2;
        else
        {
            var data = await store.Context.ContractorsData.SingleAsync();
            data.IsEuVatRegistered = invalid == "vat-missing";
            data.EuVatNumber = invalid == "vat-forbidden" ? "PL8567346215" : null;
        }
        await Assert.ThrowsAsync<DbUpdateException>(() => store.Context.SaveChangesAsync());
    }
}

internal sealed class ContractorTestStore : IAsyncDisposable
{
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    public AppDbContext Context { get; private set; } = null!;
    public ContractorRepository Repository { get; private set; } = null!;
    public ContractorService Service { get; private set; } = null!;
    public Guid TenantA { get; } = Guid.NewGuid();
    public Guid TenantB { get; } = Guid.NewGuid();
    public ReadCounter Sql { get; } = new();

    public static async Task<ContractorTestStore> CreateAsync()
    {
        var store = new ContractorTestStore();
        await store._connection.OpenAsync();
        store._connection.CreateCollation("Latin1_General_100_BIN2", (left, right) => string.Compare(left, right, StringComparison.Ordinal));
        store._connection.CreateFunction<byte[], int>("DATALENGTH", value => value.Length);
        store._connection.CreateFunction<string, int>("LEN", value => value.TrimEnd(' ').Length);
        store.Context = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlite(store._connection).AddInterceptors(store.Sql).Options);
        var schema = store.Context.Database.GenerateCreateScript().Replace("varbinary(max)", "BLOB", StringComparison.Ordinal);
        await store.Context.Database.ExecuteSqlRawAsync(schema);
        var taxEntity = new TaxEntity { Id = Guid.NewGuid(), Name = "Podmiot testowy", Nip = "8567346215" };
        store.Context.TaxEntities.Add(taxEntity);
        store.Context.Tenants.AddRange(new Tenant { Id = store.TenantA, Name = "A", TaxEntityId = taxEntity.Id }, new Tenant { Id = store.TenantB, Name = "B", TaxEntityId = taxEntity.Id });
        await store.Context.SaveChangesAsync();
        store.Context.ChangeTracker.Clear();
        store.Repository = new ContractorRepository(store.Context);
        store.Service = new ContractorService(store.Repository);
        return store;
    }

    public static CreateContractorCommand Command(bool address) => new()
    {
        FullName = "Firma", ShortName = "F", Nip = "8567346215",
        Addresses = address ? [new CreateContractorAddress { AddressType = AddressType.RegisteredOffice, City = "Poznań" }] : []
    };

    public async ValueTask DisposeAsync()
    {
        await Context.DisposeAsync();
        await _connection.DisposeAsync();
    }

    internal sealed class ReadCounter : DbCommandInterceptor
    {
        public int Reads { get; set; }
        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Reads++;
            return ValueTask.FromResult(result);
        }
    }
}
