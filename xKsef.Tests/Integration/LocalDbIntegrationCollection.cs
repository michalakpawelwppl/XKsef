namespace xKsef.Tests.Integration;

// The collection owns a fresh database per test run, never the application database.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalDbIntegrationCollection : ICollectionFixture<SqlServerTestDatabase>
{
    public const string Name = "LocalDB integration tests";
}
