using Npgsql;

namespace FuelClub.Simulator;

/// <summary>
/// Removes one run: its accounts (by id from the manifest, checked against the run's e-mail
/// pattern) with everything that belongs to them, and the offers it created when nobody else
/// uses them. Refuses any database other than the demo one. One transaction: all or nothing.
/// </summary>
public static class Cleanup
{
    public const string DemoDatabase = "fuelclub_demo";

    public static async Task<int> RunAsync(
        Manifest manifest,
        string connectionString,
        CancellationToken ct
    )
    {
        var prefix = $"sim-{manifest.RunId}-";
        var accounts = manifest
            .Accounts.Where(a => a.UserId is not null)
            .Where(a =>
                a.Email.StartsWith(prefix, StringComparison.Ordinal)
                && a.Email.EndsWith("@" + Population.EmailDomain, StringComparison.Ordinal)
            )
            .Select(a => a.UserId!)
            .ToArray();

        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(ct);
        await using (var check = new NpgsqlCommand("SELECT current_database()", connection))
        {
            var database = (string?)await check.ExecuteScalarAsync(ct);
            if (database != DemoDatabase)
            {
                Console.WriteLine(
                    $"Refused: database is '{database}', cleanup runs only on '{DemoDatabase}'."
                );
                return 2;
            }
        }

        await using var tx = await connection.BeginTransactionAsync(ct);
        // The ids must be this run's accounts: a manifest edited by hand cannot reach anyone else.
        await using (
            var guard = new NpgsqlCommand(
                """
                SELECT count(*) FROM "AspNetUsers"
                WHERE "Id" = ANY(@ids) AND ("Email" NOT LIKE @prefix OR "Email" NOT LIKE @domain)
                """,
                connection,
                tx
            )
        )
        {
            guard.Parameters.AddWithValue("ids", accounts);
            guard.Parameters.AddWithValue("prefix", prefix + "%");
            guard.Parameters.AddWithValue("domain", "%@" + Population.EmailDomain);
            if ((long)(await guard.ExecuteScalarAsync(ct))! > 0)
            {
                Console.WriteLine("Refused: the manifest lists an account outside this run.");
                return 2;
            }
        }

        // Restrict everywhere: children before parents.
        string[] statements =
        [
            """DELETE FROM "SupportMessages" WHERE "CaseId" IN (SELECT "Id" FROM "SupportCases" WHERE "UserId" = ANY(@ids)) OR "AuthorUserId" = ANY(@ids)""",
            """DELETE FROM "SupportCases" WHERE "UserId" = ANY(@ids)""",
            """DELETE FROM "PointsLedgerEntries" WHERE "UserId" = ANY(@ids)""",
            """DELETE FROM "Coupons" WHERE "UserId" = ANY(@ids)""",
            """DELETE FROM "RewardHandovers" WHERE "UserId" = ANY(@ids)""",
            """DELETE FROM "FuelPurchases" WHERE "UserId" = ANY(@ids)""",
            """DELETE FROM "LoyaltyCards" WHERE "UserId" = ANY(@ids)""",
            """DELETE FROM "LoyaltyAccounts" WHERE "UserId" = ANY(@ids)""",
            """DELETE FROM "IdempotencyRecords" WHERE "UserId" = ANY(@ids)""",
            """DELETE FROM "AspNetUsers" WHERE "Id" = ANY(@ids)""",
            """DELETE FROM "RewardOffers" o WHERE o."Id" = ANY(@offers) AND NOT EXISTS (SELECT 1 FROM "Coupons" c WHERE c."OfferId" = o."Id")""",
        ];
        var counts = new List<string>();
        foreach (var sql in statements)
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            command.Parameters.AddWithValue("ids", accounts);
            command.Parameters.AddWithValue("offers", manifest.OfferIds.ToArray());
            var rows = await command.ExecuteNonQueryAsync(ct);
            counts.Add($"{sql.Split('"')[1]} {rows}");
        }
        await tx.CommitAsync(ct);

        manifest.CleanedAt = DateTimeOffset.UtcNow;
        manifest.Save();
        Console.WriteLine($"run {manifest.RunId} removed: {string.Join(", ", counts)}");
        return 0;
    }
}
