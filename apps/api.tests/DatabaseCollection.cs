namespace FuelClub.Api.Tests;

// Tests that touch PostgreSQL share one ApiFactory and run sequentially; the factory's stale-user
// cleanup would otherwise race with users created by another test class.
[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<ApiFactory>
{
    public const string Name = "Database";
}
