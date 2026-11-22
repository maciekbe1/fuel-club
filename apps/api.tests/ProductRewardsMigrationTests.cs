using FuelClub.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace FuelClub.Api.Tests;

/// <summary>
/// Rolling back ProductRewards on a temporary database (never the shared test database): it works
/// without products and refuses, keeping the data, when product rewards exist.
/// </summary>
public sealed class ProductRewardsMigrationTests : IAsyncLifetime
{
    private const string Previous = "20261122172220_DataProtectionKeys";
    private const string ProductRewards = "20261122201440_ProductRewards";

    private readonly string _admin;
    private readonly string _connection;
    private readonly string _database = $"fuelclub_rollback_{Guid.NewGuid():N}"[..32];

    public ProductRewardsMigrationTests()
    {
        var shared =
            Environment.GetEnvironmentVariable("FUELCLUB_TEST_DATABASE")
            ?? throw new InvalidOperationException("Set FUELCLUB_TEST_DATABASE.");
        _admin = shared;
        _connection = new NpgsqlConnectionStringBuilder(shared)
        {
            Database = _database,
            Pooling = false,
        }.ConnectionString;
    }

    public async Task InitializeAsync() => await AdminAsync($"CREATE DATABASE \"{_database}\"");

    public async Task DisposeAsync() =>
        await AdminAsync($"DROP DATABASE IF EXISTS \"{_database}\" WITH (FORCE)");

    private async Task AdminAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(_admin);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(_connection).Options);

    private async Task MigrateAsync(string? target = null)
    {
        await using var db = Context();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(_connection);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    private Task<string> LastMigrationAsync() =>
        ScalarAsync<string>("""SELECT max("MigrationId") FROM "__EFMigrationsHistory" """);

    private Task<string> TermDefaultsAsync() =>
        ScalarAsync<string>(
            """
            SELECT coalesce(string_agg(table_name || '.' || column_name, ','), '')
            FROM information_schema.columns
            WHERE table_name IN ('Coupons', 'RewardOffers')
              AND column_name IN ('DiscountPerLiter', 'MaxLiters')
              AND column_default IS NOT NULL
            """
        );

    [Fact]
    public async Task Rollback_works_without_products_and_refuses_with_them_keeping_the_data()
    {
        await MigrateAsync();
        await MigrateAsync(Previous);
        Assert.Equal(Previous, await LastMigrationAsync());
        Assert.Equal(
            0L,
            await ScalarAsync<long>(
                "SELECT count(*) FROM information_schema.tables WHERE table_name = 'RewardHandovers'"
            )
        );

        // Up again: no DEFAULT 0 left behind by Down, so a product coupon keeps NULL terms.
        await MigrateAsync();
        Assert.Equal("", await TermDefaultsAsync());
        await using (var connection = new NpgsqlConnection(_connection))
        {
            await connection.OpenAsync();
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO "AspNetUsers" ("Id", "UserName", "NormalizedUserName", "Email", "NormalizedEmail",
                    "EmailConfirmed", "SecurityStamp", "ConcurrencyStamp", "PhoneNumberConfirmed",
                    "TwoFactorEnabled", "LockoutEnabled", "AccessFailedCount", "DisplayName")
                VALUES ('u1', 'rb@example.test', 'RB@EXAMPLE.TEST', 'rb@example.test', 'RB@EXAMPLE.TEST',
                    true, 's', 'c', false, false, false, 0, 'RB');
                INSERT INTO "RewardOffers" ("Id", "Name", "Description", "PointsCost", "Kind", "IsActive",
                    "CreatedAt", "UpdatedAt", "Version")
                VALUES ('00000000-0000-7000-8000-000000000001', 'Kawa', '', 150, 'Product', true, now(), now(), 1);
                INSERT INTO "Coupons" ("Id", "UserId", "OfferId", "OfferName", "PointsCost", "Kind", "Status",
                    "PurchasedAt")
                VALUES ('00000000-0000-7000-8000-0000000000c1', 'u1', '00000000-0000-7000-8000-000000000001',
                    'Kawa', 150, 'Product', 'Purchased', now());
                """,
                connection
            );
            await insert.ExecuteNonQueryAsync();
        }

        var error = await Assert.ThrowsAsync<PostgresException>(() => MigrateAsync(Previous));
        Assert.Equal("P0001", error.SqlState);
        Assert.Equal(ProductRewards, await LastMigrationAsync());
        Assert.Equal(
            1L,
            await ScalarAsync<long>(
                """SELECT count(*) FROM "RewardOffers" WHERE "Kind" = 'Product' AND "DiscountPerLiter" IS NULL"""
            )
        );
        Assert.Equal(
            1L,
            await ScalarAsync<long>("""SELECT count(*) FROM "Coupons" WHERE "Kind" = 'Product'""")
        );
    }
}
