using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using xKesf.Application.Contractors;
using xKsef.Application.Contractors;
using xKsef.Domain.Contractors;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Identity;
using xKsef.Infrastructure.Persistence;
using xKsef.Infrastructure.Persistence.Repositories;

namespace xKsef.Tests.Integration;

[Collection(LocalDbIntegrationCollection.Name)]
public sealed class ContractorSqlServerTests(SqlServerTestDatabase database) : IAsyncLifetime
{
    private readonly AppDbContext _context = database.CreateContext();
    private readonly Guid _tenantA = Guid.NewGuid();
    private readonly Guid _tenantB = Guid.NewGuid();
    private readonly Guid _taxId = Guid.NewGuid();
    private ContractorRepository Repository => new(_context);
    private ContractorService Service => new(Repository);

    public async Task InitializeAsync()
    {
        _context.TaxEntities.Add(new TaxEntity { Id = _taxId, Nip = Random.Shared.NextInt64(1_000_000_000, 10_000_000_000).ToString(), Name = "Contractor SQL tests" });
        _context.Tenants.AddRange(new Tenant { Id = _tenantA, TaxEntityId = _taxId, Name = "A" }, new Tenant { Id = _tenantB, TaxEntityId = _taxId, Name = "B" });
        await _context.SaveChangesAsync();
        _context.ChangeTracker.Clear();
    }

    [Fact]
    public async Task AllMigrationsAreAppliedAndActualCatalogColumnsMatchEfModel()
    {
        Assert.True(_context.Database.IsSqlServer());
        Assert.Empty(await _context.Database.GetPendingMigrationsAsync());
        Assert.Equal(_context.Database.GetMigrations(), await _context.Database.GetAppliedMigrationsAsync());
        Assert.False(_context.Database.HasPendingModelChanges());
        var model = _context.GetService<IDesignTimeModel>().Model;
        await _context.Database.OpenConnectionAsync();
        foreach (var entityType in new[] { typeof(Contractor), typeof(ContractorsData), typeof(Address), typeof(AddressesData) })
        {
            var entity = model.FindEntityType(entityType)!;
            var table = entity.GetTableName()!;
            using var command = _context.Database.GetDbConnection().CreateCommand();
            command.CommandText = "SELECT c.name, t.name, c.max_length, c.is_nullable FROM sys.columns c JOIN sys.types t ON c.user_type_id=t.user_type_id WHERE c.object_id=OBJECT_ID(@table)";
            var parameter = command.CreateParameter(); parameter.ParameterName = "@table"; parameter.Value = "dbo." + table; command.Parameters.Add(parameter);
            var columns = new Dictionary<string, (string Type, bool Nullable)>();
            await using (var reader = await command.ExecuteReaderAsync())
            {
                while (await reader.ReadAsync())
                {
                    var type = reader.GetString(1);
                    if (type == "nvarchar") type += $"({reader.GetInt16(2) / 2})";
                    columns.Add(reader.GetString(0), (type, reader.GetBoolean(3)));
                }
            }
            Assert.Equal(entity.GetProperties().Count(), columns.Count);
            foreach (var property in entity.GetProperties())
            {
                var actual = columns[property.GetColumnName(StoreObjectIdentifier.Table(table, entity.GetSchema()))!];
                Assert.Equal(property.GetColumnType(), actual.Type);
                Assert.Equal(property.IsNullable, actual.Nullable);
            }
        }
        using var checks = _context.Database.GetDbConnection().CreateCommand();
        checks.CommandText = "SELECT COUNT(*) FROM sys.check_constraints WHERE parent_object_id IN (OBJECT_ID('dbo.ContractorsData'),OBJECT_ID('dbo.Addresses')) AND is_disabled=0 AND is_not_trusted=0";
        Assert.Equal(4, Convert.ToInt32(await checks.ExecuteScalarAsync()));
        checks.CommandText = "SELECT COUNT(*) FROM sys.foreign_keys WHERE parent_object_id IN (OBJECT_ID('dbo.Contractors'),OBJECT_ID('dbo.ContractorsData'),OBJECT_ID('dbo.Addresses'),OBJECT_ID('dbo.AddressesData')) AND delete_referential_action=0 AND is_disabled=0 AND is_not_trusted=0";
        Assert.Equal(4, Convert.ToInt32(await checks.ExecuteScalarAsync()));
        checks.CommandText = "SELECT COUNT(*) FROM sys.default_constraints WHERE (parent_object_id=OBJECT_ID('dbo.ContractorsData') AND COL_NAME(parent_object_id,parent_column_id)='ShortName') OR (parent_object_id=OBJECT_ID('dbo.Addresses') AND COL_NAME(parent_object_id,parent_column_id)='AddressType')";
        Assert.Equal(0, Convert.ToInt32(await checks.ExecuteScalarAsync()));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CreateRoundTripsAllFieldsAndOptionalRegisteredOffice(bool address)
    {
        var id = await Service.CreateAsync(_tenantA, Command(address));
        var result = (await Repository.GetByIdAsync(_tenantA, id))!;
        Assert.True(result.Contractor.IsActive);
        Assert.Equal("Firma", result.ContractorData.FullName);
        Assert.Equal("F", result.ContractorData.ShortName);
        Assert.Equal("8567346215", result.ContractorData.Nip);
        Assert.Equal("012345675", result.ContractorData.Regon);
        Assert.Equal("0000123456", result.ContractorData.KrsNumber);
        Assert.Equal("PL8567346215", result.ContractorData.EuVatNumber);
        Assert.Equal("OTHER", result.ContractorData.OtherTaxNumber);
        Assert.True(result.ContractorData.IsEuVatRegistered);
        Assert.Equal(address ? 1 : 0, result.Addresses.Count);
    }

    [Fact]
    public async Task UpdateByTypePreservesIdsAndActivationAndSeparatesTenants()
    {
        var id = await Service.CreateAsync(_tenantA, Command(true));
        var foreignId = await Service.CreateAsync(_tenantB, Command(true));
        var original = (await Repository.GetByIdAsync(_tenantA, id))!;
        foreach (var active in new[] { false, true })
        {
            await Service.UpdateAsync(_tenantA, id, new UpdateContractorCommand
            {
                FullName = "Nowa nazwa", ShortName = "N", IsActive = active,
                Addresses = [new CreateContractorAddress { AddressType = AddressType.RegisteredOffice, City = "Warszawa" }]
            });
            var updated = (await Repository.GetByIdAsync(_tenantA, id))!;
            Assert.Equal(original.Contractor.Id, updated.Contractor.Id);
            Assert.Equal(original.Contractor.CreatedAt, updated.Contractor.CreatedAt);
            Assert.Equal(original.ContractorData.Id, updated.ContractorData.Id);
            Assert.Equal(original.Addresses[0].Address.Id, Assert.Single(updated.Addresses).Address.Id);
            Assert.Equal(original.Addresses[0].AddressData.Id, updated.Addresses[0].AddressData.Id);
            Assert.Equal("Warszawa", updated.Addresses[0].AddressData.City);
            Assert.Equal(active, updated.Contractor.IsActive);
            Assert.Single(await Repository.GetContractorsAsync(_tenantA));
        }
        Assert.Null(await Repository.GetByIdAsync(_tenantA, foreignId));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => Service.UpdateAsync(_tenantA, foreignId, new UpdateContractorCommand { FullName = "X", ShortName = "X", IsActive = false }));
        Assert.True((await Repository.GetByIdAsync(_tenantB, foreignId))!.Contractor.IsActive);
        await Service.UpdateAsync(_tenantA, id, new UpdateContractorCommand { FullName = "Bez adresu", ShortName = "B", IsActive = true, Addresses = [] });
        Assert.Empty((await Repository.GetByIdAsync(_tenantA, id))!.Addresses);
    }

    [Fact]
    public async Task SqlUniqueIndexRejectsDuplicateTypeButAllowsSameTypeForAnotherContractor()
    {
        var id = await Service.CreateAsync(_tenantA, Command(true));
        await Service.CreateAsync(_tenantA, Command(true));
        var existing = (await Repository.GetByIdAsync(_tenantA, id))!;
        _context.Addresses.Add(new Address { Id = Guid.NewGuid(), ContractorsDataId = existing.ContractorData.Id, AddressType = AddressType.RegisteredOffice });
        await AssertSqlFailureAsync(() => _context.SaveChangesAsync(), 2601, 2627);
    }

    [Theory]
    [InlineData(true, null, false)]
    [InlineData(true, "", false)]
    [InlineData(true, "   ", false)]
    [InlineData(false, "PL8567346215", false)]
    [InlineData(false, "", false)]
    [InlineData(false, null, true)]
    [InlineData(true, "PL8567346215", true)]
    public async Task SqlVatConstraintEnforcesBothDirections(bool registered, string? number, bool valid)
    {
        var id = await Service.CreateAsync(_tenantA, Command(false));
        var data = await _context.ContractorsData.SingleAsync(item => item.ContractorId == id);
        data.IsEuVatRegistered = registered;
        data.EuVatNumber = number;
        if (valid) await _context.SaveChangesAsync();
        else await AssertSqlFailureAsync(() => _context.SaveChangesAsync(), 547);
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("contractor")]
    [InlineData("contractor-data")]
    [InlineData("address")]
    public async Task SqlForeignKeysRejectOrphans(string parent)
    {
        switch (parent)
        {
            case "tenant": _context.Contractors.Add(new Contractor { Id = Guid.NewGuid(), TenantId = Guid.NewGuid(), CreatedAt = DateTimeOffset.UtcNow }); break;
            case "contractor": _context.ContractorsData.Add(new ContractorsData { Id = Guid.NewGuid(), ContractorId = Guid.NewGuid(), FullName = "F", ShortName = "F" }); break;
            case "contractor-data": _context.Addresses.Add(new Address { Id = Guid.NewGuid(), ContractorsDataId = Guid.NewGuid(), AddressType = AddressType.RegisteredOffice }); break;
            case "address": _context.AddressesData.Add(new AddressesData { Id = Guid.NewGuid(), AddressId = Guid.NewGuid(), City = "A" }); break;
        }
        await AssertSqlFailureAsync(() => _context.SaveChangesAsync(), 547);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(2)]
    public async Task SqlTypeConstraintRejectsUndefinedValues(int type)
    {
        var id = await Service.CreateAsync(_tenantA, Command(false));
        var existing = (await Repository.GetByIdAsync(_tenantA, id))!;
        _context.Addresses.Add(new Address { Id = Guid.NewGuid(), ContractorsDataId = existing.ContractorData.Id, AddressType = (AddressType)type });
        await AssertSqlFailureAsync(() => _context.SaveChangesAsync(), 547);
    }

    [Fact]
    public async Task SqlRestrictPreventsDeletingParentWithDependents()
    {
        var id = await Service.CreateAsync(_tenantA, Command(true));
        var exception = await Assert.ThrowsAsync<SqlException>(() => _context.Database.ExecuteSqlInterpolatedAsync($"DELETE FROM Contractors WHERE Id={id}"));
        Assert.Equal(547, exception.Number);
        Assert.NotNull(await Repository.GetByIdAsync(_tenantA, id));
    }

    [Fact]
    public async Task UpgradeFromLegacyCatalogClearsOnlyCatalogAndSupportsDowngradeAndReapply()
    {
        var isolated = await SqlServerTestDatabase.CreateEmptyAsync();
        try
        {
            await using var context = isolated.CreateContext();
            var migrator = context.GetService<IMigrator>();
            await migrator.MigrateAsync("20261119181911_AddContractorsCatalog");
            var tax = Guid.NewGuid(); var tenant = Guid.NewGuid(); var contractor = Guid.NewGuid(); var data = Guid.NewGuid(); var address = Guid.NewGuid(); var details = Guid.NewGuid();
            await context.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO TaxEntities (Id,Nip,Name) VALUES ({tax},N'8567346215',N'Keep'); INSERT INTO Tenants (Id,Name,IsActive,TaxEntityId) VALUES ({tenant},N'Keep',1,{tax}); INSERT INTO Contractors (Id,TenantId,IsActive,CreatedAt) VALUES ({contractor},{tenant},0,SYSDATETIMEOFFSET()); INSERT INTO ContractorsData (Id,ContractorId,Name,Nip) VALUES ({data},{contractor},N'Legacy',N'8567346215'); INSERT INTO Addresses (Id,ContractorsDataId) VALUES ({address},{data}); INSERT INTO AddressesData (Id,AddressId,CountryCode,Street,BuildingNumber,PostalCode,City) VALUES ({details},{address},N'PL',N'',N'',N'',N'Legacy');");
            var user = new ApplicationUser { Id = Guid.NewGuid().ToString(), UserName = "Keep" };
            context.Users.Add(user);
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await migrator.MigrateAsync();
            Assert.Empty(await context.Contractors.ToListAsync());
            Assert.Empty(await context.ContractorsData.ToListAsync());
            Assert.Empty(await context.Addresses.ToListAsync());
            Assert.Empty(await context.AddressesData.ToListAsync());
            Assert.Equal(tenant, (await context.Tenants.SingleAsync()).Id);
            Assert.Equal(tax, (await context.TaxEntities.SingleAsync()).Id);
            Assert.Equal(user.Id, (await context.Users.SingleAsync()).Id);
            await migrator.MigrateAsync("20261119181911_AddContractorsCatalog");
            await migrator.MigrateAsync();
            Assert.Empty(await context.Database.GetPendingMigrationsAsync());
        }
        finally { await isolated.DisposeAsync(); }
    }

    private static CreateContractorCommand Command(bool address) => new()
    {
        FullName = "Firma", ShortName = "F", Nip = "8567346215", Regon = "012345675", KrsNumber = "0000123456",
        EuVatNumber = "PL8567346215", IsEuVatRegistered = true, OtherTaxNumber = "OTHER",
        Addresses = address ? [new CreateContractorAddress { AddressType = AddressType.RegisteredOffice, City = "Poznań" }] : []
    };

    private static async Task AssertSqlFailureAsync(Func<Task> operation, params int[] numbers)
    {
        var exception = await Assert.ThrowsAsync<DbUpdateException>(operation);
        Assert.Contains(Assert.IsType<SqlException>(exception.InnerException).Number, numbers);
    }

    public async Task DisposeAsync()
    {
        _context.ChangeTracker.Clear();
        var contractors = await _context.Contractors.Where(item => item.TenantId == _tenantA || item.TenantId == _tenantB).Select(item => item.Id).ToArrayAsync();
        var data = await _context.ContractorsData.Where(item => contractors.Contains(item.ContractorId)).Select(item => item.Id).ToArrayAsync();
        var addresses = await _context.Addresses.Where(item => data.Contains(item.ContractorsDataId)).Select(item => item.Id).ToArrayAsync();
        await _context.AddressesData.Where(item => addresses.Contains(item.AddressId)).ExecuteDeleteAsync();
        await _context.Addresses.Where(item => addresses.Contains(item.Id)).ExecuteDeleteAsync();
        await _context.ContractorsData.Where(item => data.Contains(item.Id)).ExecuteDeleteAsync();
        await _context.Contractors.Where(item => contractors.Contains(item.Id)).ExecuteDeleteAsync();
        await _context.Tenants.Where(item => item.Id == _tenantA || item.Id == _tenantB).ExecuteDeleteAsync();
        await _context.TaxEntities.Where(item => item.Id == _taxId).ExecuteDeleteAsync();
        await _context.DisposeAsync();
    }
}
