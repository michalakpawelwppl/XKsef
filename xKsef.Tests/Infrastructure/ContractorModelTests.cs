using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using xKsef.Application.Contractors;
using xKsef.Domain.Contractors;
using xKsef.Domain.Tenants;
using xKsef.Infrastructure.Persistence;

namespace xKsef.Tests.Infrastructure;

public sealed class ContractorModelTests
{
    [Theory]
    [InlineData(nameof(ContractorsData.FullName), 500, false)]
    [InlineData(nameof(ContractorsData.ShortName), 100, false)]
    [InlineData(nameof(ContractorsData.Nip), 20, true)]
    [InlineData(nameof(ContractorsData.Regon), 14, true)]
    [InlineData(nameof(ContractorsData.KrsNumber), 10, true)]
    [InlineData(nameof(ContractorsData.EuVatNumber), 32, true)]
    [InlineData(nameof(ContractorsData.OtherTaxNumber), 100, true)]
    public void ContractorStringsHaveExpectedNullabilityAndLengths(string name, int length, bool nullable)
    {
        using var context = CreateContext();
        var property = Entity<ContractorsData>(context).FindProperty(name)!;

        Assert.Equal(typeof(string), property.ClrType);
        Assert.Equal(nullable, property.IsNullable);
        Assert.Equal(length, property.GetMaxLength());
        Assert.Equal($"nvarchar({length})", property.GetColumnType());
        Assert.Null(Entity<ContractorsData>(context).FindProperty("Name"));
    }

    [Fact]
    public void EuVatRegistrationDefaultsToFalseInModelAndEntity()
    {
        using var context = CreateContext();
        var property = Entity<ContractorsData>(context).FindProperty(nameof(ContractorsData.IsEuVatRegistered))!;

        Assert.False(property.IsNullable);
        Assert.Equal("bit", property.GetColumnType());
        Assert.Equal(false, property.GetDefaultValue());
        Assert.False(new ContractorsData().IsEuVatRegistered);
    }

    [Fact]
    public void AddressBelongsToOneContractorDataAndUsesUniqueOwnerTypeIndex()
    {
        using var context = CreateContext();
        var entity = Entity<Address>(context);
        var foreignKey = Assert.Single(entity.GetForeignKeys());

        Assert.Equal(typeof(ContractorsData), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(nameof(Address.ContractorsDataId), Assert.Single(foreignKey.Properties).Name);
        Assert.True(foreignKey.IsRequired);
        Assert.False(foreignKey.IsUnique);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        var index = Assert.Single(entity.GetIndexes());
        Assert.True(index.IsUnique);
        Assert.Equal(new[] { nameof(Address.ContractorsDataId), nameof(Address.AddressType) }, index.Properties.Select(property => property.Name));
        Assert.DoesNotContain(Entity<ContractorsData>(context).GetForeignKeys(), key => key.PrincipalEntityType.ClrType == typeof(Address));
    }

    [Fact]
    public void AddressTypeIsRequiredIntegerWithoutImplicitRegisteredOfficeDefault()
    {
        using var context = CreateContext();
        var property = Entity<Address>(context).FindProperty(nameof(Address.AddressType))!;

        Assert.Equal(typeof(AddressType), property.ClrType);
        Assert.False(property.IsNullable);
        Assert.Equal("int", property.GetColumnType());
        Assert.Null(property.FindAnnotation(RelationalAnnotationNames.DefaultValue));
        Assert.Equal(1, (int)AddressType.RegisteredOffice);
        Assert.False(Enum.IsDefined(new Address().AddressType));
    }

    [Theory]
    [InlineData(typeof(ContractorsData), typeof(Contractor), nameof(ContractorsData.ContractorId))]
    [InlineData(typeof(AddressesData), typeof(Address), nameof(AddressesData.AddressId))]
    public void ExistingOneToOneForeignKeysAndUniqueIndexesArePreserved(Type dependent, Type principal, string propertyName)
    {
        using var context = CreateContext();
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(dependent)!;
        var foreignKey = Assert.Single(entity.GetForeignKeys());

        Assert.Equal(principal, foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(propertyName, Assert.Single(foreignKey.Properties).Name);
        Assert.True(foreignKey.IsRequired);
        Assert.True(foreignKey.IsUnique);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        var index = Assert.Single(entity.GetIndexes());
        Assert.True(index.IsUnique);
        Assert.Equal(propertyName, Assert.Single(index.Properties).Name);
    }

    [Fact]
    public void TenantRelationshipActivationAndCreationDateArePreserved()
    {
        using var context = CreateContext();
        var contractor = Entity<Contractor>(context);
        var foreignKey = Assert.Single(contractor.GetForeignKeys());

        Assert.Equal(typeof(Tenant), foreignKey.PrincipalEntityType.ClrType);
        Assert.Equal(nameof(Contractor.TenantId), Assert.Single(foreignKey.Properties).Name);
        Assert.True(foreignKey.IsRequired);
        Assert.Equal(DeleteBehavior.Restrict, foreignKey.DeleteBehavior);
        var index = Assert.Single(contractor.GetIndexes());
        Assert.Equal(nameof(Contractor.TenantId), Assert.Single(index.Properties).Name);
        Assert.False(index.IsUnique);
        Assert.Equal(true, contractor.FindProperty(nameof(Contractor.IsActive))!.GetDefaultValue());
        Assert.False(contractor.FindProperty(nameof(Contractor.CreatedAt))!.IsNullable);
        Assert.True(new Contractor().IsActive);
    }

    [Theory]
    [InlineData(nameof(AddressesData.CountryCode), 2, false)]
    [InlineData(nameof(AddressesData.Street), 200, false)]
    [InlineData(nameof(AddressesData.BuildingNumber), 20, false)]
    [InlineData(nameof(AddressesData.ApartmentNumber), 20, true)]
    [InlineData(nameof(AddressesData.PostalCode), 20, false)]
    [InlineData(nameof(AddressesData.City), 200, false)]
    public void AddressDataFieldsKeepTheirExistingMapping(string name, int length, bool nullable)
    {
        using var context = CreateContext();
        var property = Entity<AddressesData>(context).FindProperty(name)!;

        Assert.Equal(nullable, property.IsNullable);
        Assert.Equal(length, property.GetMaxLength());
        Assert.Empty(Entity<AddressesData>(context).GetCheckConstraints());
    }

    [Fact]
    public void AggregateSupportsNoAddressesAndPairsAddressRecordsWithTheirData()
    {
        Assert.Empty(new ContractorAggregate().Addresses);
        var address = new Address { Id = Guid.NewGuid(), AddressType = AddressType.RegisteredOffice };
        var data = new AddressesData { Id = Guid.NewGuid(), AddressId = address.Id };
        var aggregate = new ContractorAggregate
        {
            Addresses = [new ContractorAddressAggregate { Address = address, AddressData = data }]
        };

        var item = Assert.Single(aggregate.Addresses);
        Assert.Same(address, item.Address);
        Assert.Same(data, item.AddressData);
        Assert.Equal(item.Address.Id, item.AddressData.AddressId);
    }

    [Fact]
    public void SqlServerSchemaIncludesVatTypeAndNonemptyNameConstraintsWithoutConnecting()
    {
        using var context = CreateContext();
        var constraints = Entity<ContractorsData>(context).GetCheckConstraints().ToDictionary(constraint => constraint.Name!, constraint => constraint.Sql);
        var vat = constraints["CK_ContractorsData_EuVatRegistration"];

        Assert.Contains("[IsEuVatRegistered] = 0 AND [EuVatNumber] IS NULL", vat);
        Assert.Contains("[IsEuVatRegistered] = 1 AND [EuVatNumber] IS NOT NULL", vat);
        Assert.Contains("LEN(LTRIM(RTRIM([EuVatNumber]))) > 0", vat);
        Assert.Contains("[FullName]", constraints["CK_ContractorsData_FullName_NotEmpty"]);
        Assert.Contains("[ShortName]", constraints["CK_ContractorsData_ShortName_NotEmpty"]);
        Assert.Equal("[AddressType] IN (1)", Assert.Single(Entity<Address>(context).GetCheckConstraints()).Sql);

        var sql = context.Database.GenerateCreateScript();
        foreach (var name in constraints.Keys.Append("CK_Addresses_AddressType"))
            Assert.Contains($"CONSTRAINT [{name}] CHECK", sql);
        Assert.Contains("CREATE UNIQUE INDEX [IX_Addresses_ContractorsDataId_AddressType]", sql);
        Assert.DoesNotContain("CREATE UNIQUE INDEX [IX_Addresses_ContractorsDataId]", sql);
        Assert.Equal(ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private static IEntityType Entity<T>(AppDbContext context) => context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(T))!;

    private static AppDbContext CreateContext()
    {
        // Inspect metadata and generate SQL only; never open a database connection.
        return new AppDbContext(new DbContextOptionsBuilder<AppDbContext>().UseSqlServer("Server=offline.invalid;Database=OfflineModelOnly;Integrated Security=true;Connect Timeout=1").Options);
    }
}
