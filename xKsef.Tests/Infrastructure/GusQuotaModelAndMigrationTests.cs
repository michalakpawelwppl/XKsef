using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using xKsef.Application.Gus;
using xKsef.Infrastructure;
using xKsef.Infrastructure.Gus;
using xKsef.Infrastructure.Migrations;
using xKsef.Infrastructure.Persistence;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace xKsef.Tests.Infrastructure;

public sealed class GusQuotaModelAndMigrationTests
{
    [Fact]
    public void TechnicalTableHasOnlyRequiredColumnsIdentityIndexAndEnvironmentCheck()
    {
        using var context = new AppDbContextFactory().CreateDbContext([]);
        var entity = context.GetService<IDesignTimeModel>().Model.FindEntityType(typeof(GusCallPermitEntry))!;
        Assert.Equal("GusCallPermits", entity.GetTableName());
        Assert.Equal("dbo", entity.GetSchema());
        Assert.Equal(3, entity.GetProperties().Count());
        Assert.Empty(entity.GetForeignKeys());
        Assert.Empty(entity.GetDeclaredQueryFilters());
        var id = entity.FindProperty(nameof(GusCallPermitEntry.Id))!;
        Assert.Equal("bigint", id.GetColumnType());
        Assert.Equal(SqlServerValueGenerationStrategy.IdentityColumn, SqlServerPropertyExtensions.GetValueGenerationStrategy(id));
        Assert.Equal("tinyint", entity.FindProperty(nameof(GusCallPermitEntry.Environment))!.GetColumnType());
        Assert.Equal("datetime2(7)", entity.FindProperty(nameof(GusCallPermitEntry.GrantedAtUtc))!.GetColumnType());
        Assert.All(entity.GetProperties(), p => Assert.False(p.IsNullable));
        Assert.Equal(new[] { "Environment", "GrantedAtUtc" }, Assert.Single(entity.GetIndexes()).Properties.Select(p => p.Name));
        Assert.Equal("[Environment] IN (0, 1)", Assert.Single(entity.GetCheckConstraints()).Sql);
        Assert.Equal(0, (int)GusRateLimitEnvironment.Test);
        Assert.Equal(1, (int)GusRateLimitEnvironment.Production);
    }

    [Fact]
    public void MigrationAddsOnlyTheTechnicalTableAndIndexAndDownDropsOnlyThatTable()
    {
        var migration = new AddGusCallPermits();
        Assert.All(migration.UpOperations, op => Assert.True(op is EnsureSchemaOperation or CreateTableOperation or CreateIndexOperation));
        var table = Assert.Single(migration.UpOperations.OfType<CreateTableOperation>());
        Assert.Equal("GusCallPermits", table.Name);
        Assert.Equal(3, table.Columns.Count);
        Assert.Equal("1, 1", Assert.Single(table.Columns, column => column.Name == "Id")["SqlServer:Identity"]);
        Assert.Single(table.CheckConstraints);
        Assert.Empty(table.ForeignKeys);
        Assert.All(table.Columns, column => { Assert.False(column.IsNullable); Assert.Null(column.DefaultValue); Assert.Null(column.DefaultValueSql); });
        Assert.Equal(new[] { "Environment", "GrantedAtUtc" }, Assert.Single(migration.UpOperations.OfType<CreateIndexOperation>()).Columns);
        Assert.Equal("GusCallPermits", Assert.IsType<DropTableOperation>(Assert.Single(migration.DownOperations)).Name);
        using var context = new AppDbContextFactory().CreateDbContext([]);
        var sql = context.GetService<IMigrator>().GenerateScript("20261121100931_ExpandContractorsCatalog", "20261121120514_AddGusCallPermits");
        Assert.Contains("CREATE TABLE [dbo].[GusCallPermits]", sql);
        Assert.Contains("CHECK ([Environment] IN (0, 1))", sql);
        Assert.DoesNotMatch(@"(?im)^\s*(DELETE|UPDATE|TRUNCATE|DROP)\b", sql);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    [Fact]
    public void DatabaseRegistrationUsesExistingConnectionAndPreservesClockOverride()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:DefaultConnection"] = "Server=offline.invalid;Database=NoConnection;Integrated Security=true"
        }).Build();
        var services = new ServiceCollection();
        var clock = new TestClock();
        services.AddSingleton<TimeProvider>(clock);
        services.AddLogging();
        services.AddDatabaseInfrastructure(configuration);
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        Assert.IsType<SqlGusQuotaStore>(scope.ServiceProvider.GetRequiredService<IGusQuotaStore>());
        Assert.Same(clock, scope.ServiceProvider.GetRequiredService<TimeProvider>());
        Assert.IsType<GusRateLimiter>(scope.ServiceProvider.GetRequiredService<IGusRateLimiter>());
        Assert.Same(scope.ServiceProvider.GetRequiredService<IGusRateLimiter>(), scope.ServiceProvider.GetRequiredService<IGusRateLimiter>());
        var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var configured = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(configuration.GetConnectionString("DefaultConnection"));
        var actual = new Microsoft.Data.SqlClient.SqlConnectionStringBuilder(context.Database.GetConnectionString());
        // EF adds its own Application Name; the configured target/authentication must remain unchanged.
        Assert.Equal(configured.DataSource, actual.DataSource);
        Assert.Equal(configured.InitialCatalog, actual.InitialCatalog);
        Assert.Equal(configured.IntegratedSecurity, actual.IntegratedSecurity);
        Assert.Equal(System.Data.ConnectionState.Closed, context.Database.GetDbConnection().State);
    }

    private sealed class TestClock : TimeProvider;
}
