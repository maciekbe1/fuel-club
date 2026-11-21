# API integration tests

The tests require a dedicated PostgreSQL database named `fuelclub_test`. They reject any other database name to protect developer data.

Start the isolated test database from the repository root:

```sh
docker compose up -d postgres-test
```

Then run the tests:

```sh
FUELCLUB_TEST_DATABASE='Host=localhost;Port=5434;Database=fuelclub_test;Username=fuelclub_test;Password=fuelclub_test' dotnet test apps/api.tests/FuelClub.Api.Tests.csproj
```
