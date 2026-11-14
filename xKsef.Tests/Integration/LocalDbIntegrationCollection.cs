namespace xKsef.Tests.Integration;

// Fixtures share a physical LocalDB. Parallel requests inside individual tests remain enabled.
[CollectionDefinition(Name, DisableParallelization = true)]
public sealed class LocalDbIntegrationCollection
{
    public const string Name = "LocalDB integration tests";
}