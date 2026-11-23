using System.Text.RegularExpressions;
using Npgsql;

namespace FuelClub.Simulator;

/// <summary>
/// Removes one run: its accounts (found by the run's exact e-mail addresses) with everything that
/// belongs to them, the run's idempotency records, and the offers it created when nobody else
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
        // Exactly this run's addresses (a run id that prefixes another must not reach it). Users
        // are found by address, so an account registered just before a crash, with no user id in
        // the manifest yet, goes too.
        var pattern =
            $"^sim-{Regex.Escape(manifest.RunId)}-[0-9]{{4}}@{Regex.Escape(Population.EmailDomain)}$";
        var emails = manifest.Accounts.Select(a => a.Email).ToArray();
        if (emails.Any(e => !Regex.IsMatch(e, pattern)))
        {
            Console.WriteLine("Refused: the manifest lists an account outside this run.");
            return 2;
        }
        var keys = manifest
            .Operations.Select(o => Guid.TryParse(o.IdempotencyKey, out var key) ? key : Guid.Empty)
            .Where(k => k != Guid.Empty)
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
        string[] accounts;
        await using (
            var find = new NpgsqlCommand(
                """SELECT "Id" FROM "AspNetUsers" WHERE "Email" = ANY(@emails) AND "Email" ~ @pattern""",
                connection,
                tx
            )
        )
        {
            find.Parameters.AddWithValue("emails", emails);
            find.Parameters.AddWithValue("pattern", pattern);
            var found = new List<string>();
            await using var reader = await find.ExecuteReaderAsync(ct);
            while (await reader.ReadAsync(ct))
                found.Add(reader.GetString(0));
            accounts = [.. found];
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
            // The customers' own records, and the run's operations recorded under the cashier and
            // the admin (purchases, redemptions, offers) by their keys.
            """DELETE FROM "IdempotencyRecords" WHERE "UserId" = ANY(@ids) OR "IdempotencyKey" = ANY(@keys)""",
            """DELETE FROM "AspNetUsers" WHERE "Id" = ANY(@ids)""",
            """DELETE FROM "RewardOffers" o WHERE o."Id" = ANY(@offers) AND NOT EXISTS (SELECT 1 FROM "Coupons" c WHERE c."OfferId" = o."Id")""",
        ];
        var counts = new List<string>();
        foreach (var sql in statements)
        {
            await using var command = new NpgsqlCommand(sql, connection, tx);
            command.Parameters.AddWithValue("ids", accounts);
            command.Parameters.AddWithValue("offers", manifest.OfferIds.ToArray());
            command.Parameters.AddWithValue("keys", keys);
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
