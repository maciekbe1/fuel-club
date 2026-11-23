using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace FuelClub.Simulator;

/// <summary>
/// A controlled check of coupon selection (the demo-1 coupon bug): the same data on a fresh
/// instance, the same 11 cases, the outcome read from the confirmation event (which offer), never
/// from the reply's wording. Run it on one fresh database per adapter version and compare.
/// No proposal is confirmed, so the cases do not change each other's data.
/// </summary>
public static class CouponCases
{
    public const string Rabat = "CMP rabat";
    public const string HotDog = "CMP hot dog";
    public const string Kawa = "CMP kawa";
    public const string KawaDuza = "CMP kawa duża";

    /// <summary>Offers of the check and the Purchased coupons each account holds.</summary>
    public static readonly (string Name, string Kind)[] Offers =
    [
        (Rabat, "FuelDiscount"),
        (HotDog, "Product"),
        (Kawa, "Product"),
        (KawaDuza, "Product"),
    ];

    public static readonly Dictionary<string, string[]> Holdings = new()
    {
        ["a"] = [HotDog],
        ["b"] = [Rabat, HotDog, Kawa, KawaDuza],
        ["c"] = [],
    };

    /// <summary>One case; an ID in the message comes from <c>IdOf</c> (account, offer).</summary>
    public sealed record Case(
        string Id,
        string Account,
        string Rule,
        string Message,
        string[] Expected,
        (string Account, string Offer)? IdOf = null
    );

    public static readonly Case[] All =
    [
        new("1", "a", "nazwany, klient go nie ma (ma jeden inny)", $"Aktywuj kupon {Rabat}", []),
        new("2", "a", "ogólna prośba, jedyny kupon", "Aktywuj mój kupon", [HotDog]),
        new("3", "a", "nazwa w odmianie", "Chcę aktywować kupon na hot doga", [HotDog]),
        new("4", "a", "nazwa, której klient nie ma", "Aktywuj kupon kawa", []),
        new("5", "b", "nazwany, klient go ma (kilka kuponów)", $"Aktywuj kupon {Rabat}", [Rabat]),
        new("6", "b", "ogólna prośba, kilka kuponów - dopytanie", "Aktywuj mój kupon", []),
        new("7", "b", "nazwa pasuje do dwóch ofert - dopytanie", "Aktywuj kupon kawa", []),
        new("8", "b", "pełna nazwa jednej z pasujących", "Aktywuj kupon kawa duża", [KawaDuza]),
        new("9", "b", "własne ID", "Aktywuj kupon {id}", [Kawa], ("b", Kawa)),
        new("10", "b", "cudze ID", "Aktywuj kupon {id}", [], ("a", HotDog)),
        new("11", "c", "brak kuponów", $"Aktywuj kupon {Rabat}", []),
    ];

    public sealed record Result(
        string Case,
        string Rule,
        string Message,
        string[] Expected,
        string[] Proposals,
        bool Ok,
        string Reply
    );

    public sealed record ResultFile(
        string RunId,
        string Provider,
        string Model,
        DateTimeOffset At,
        List<Result> Results
    );

    /// <summary>Exactly the expected proposals (offer names, in order); none when none expected.</summary>
    public static bool Matches(string[] expected, IReadOnlyList<string> proposals) =>
        expected.SequenceEqual(proposals);

    public static async Task<int> RunAsync(
        string api,
        string run,
        string folder,
        Settings settings,
        string provider,
        string model,
        CancellationToken ct
    )
    {
        using var admin = await SignInAsync(api, settings.AdminEmail, settings.AdminPassword, ct);
        using var cashier = await SignInAsync(
            api,
            settings.CashierEmail,
            settings.CashierPassword,
            ct
        );
        // Identical data means a fresh database: an offer of this check already there refuses.
        var catalog = await admin.GetAsync<JsonElement>("/api/admin/rewards", ct);
        if (
            catalog
                .GetProperty("items")
                .EnumerateArray()
                .Any(o => Offers.Any(x => x.Name == o.GetProperty("name").GetString()))
        )
        {
            Console.WriteLine(
                "Refused: the instance already has the check's offers (use a fresh database)."
            );
            return 2;
        }
        foreach (var (name, kind) in Offers)
        {
            using var response = await admin.PostAsync(
                "/api/admin/rewards",
                new
                {
                    name,
                    description = "Kontrolowane sprawdzenie wyboru kuponu",
                    pointsCost = 10,
                    kind,
                    discountPerLiter = kind == "FuelDiscount" ? (decimal?)0.15m : null,
                    maxLiters = kind == "FuelDiscount" ? (decimal?)40m : null,
                    isActive = true,
                },
                ct,
                Guid.NewGuid().ToString()
            );
            await ApiSession.EnsureAsync(response, $"offer {name}", ct);
        }
        var offers = (await admin.GetAsync<JsonElement>("/api/admin/rewards", ct))
            .GetProperty("items")
            .EnumerateArray()
            .Where(o => Offers.Any(x => x.Name == o.GetProperty("name").GetString()))
            .ToDictionary(
                o => o.GetProperty("name").GetString()!,
                o =>
                    (
                        Id: o.GetProperty("id").GetGuid(),
                        Version: o.GetProperty("version").GetInt32()
                    )
            );

        var sessions = new Dictionary<string, ApiSession>();
        var coupons = new Dictionary<(string, string), Guid>();
        try
        {
            foreach (var (account, holds) in Holdings)
            {
                var session = await AccountAsync(api, run, account, settings.Secret, cashier, ct);
                sessions[account] = session;
                foreach (var name in holds)
                {
                    using var response = await session.PostAsync(
                        $"/api/rewards/{offers[name].Id}/exchange",
                        new { version = offers[name].Version },
                        ct,
                        Guid.NewGuid().ToString()
                    );
                    await ApiSession.EnsureAsync(response, $"exchange {name}", ct);
                    coupons[(account, name)] = (
                        await response.Content.ReadFromJsonAsync<JsonElement>(ct)
                    )
                        .GetProperty("coupon")
                        .GetProperty("id")
                        .GetGuid();
                }
            }

            var results = new List<Result>();
            foreach (var c in All)
            {
                var message = c.IdOf is { } of
                    ? c.Message.Replace("{id}", coupons[of].ToString())
                    : c.Message;
                var turn = await AskAsync(sessions[c.Account], message, ct);
                results.Add(
                    new(
                        c.Id,
                        c.Rule,
                        // The id differs per database: the report shows the case, not the guid.
                        c.IdOf
                            is null
                            ? message
                            : c.Message,
                        c.Expected,
                        [.. turn.Proposals],
                        Matches(c.Expected, turn.Proposals) && turn.Done,
                        turn.Assistant
                    )
                );
            }
            Directory.CreateDirectory(folder);
            var file = new ResultFile(run, provider, model, DateTimeOffset.UtcNow, results);
            File.WriteAllText(
                Path.Combine(folder, "coupon-cases.json"),
                JsonSerializer.Serialize(file, Manifest.Json)
            );
            Console.WriteLine(
                $"run {run}: {results.Count(r => r.Ok)}/{results.Count} coupon cases as expected"
            );
            return 0;
        }
        finally
        {
            foreach (var session in sessions.Values)
                session.Dispose();
        }
    }

    /// <summary>Side by side: the same cases from two runs (e.g. v7 and v8 on fresh databases).</summary>
    public static string Report(ResultFile before, ResultFile after)
    {
        var text = new StringBuilder();
        text.AppendLine($"# Wybór kuponu - {before.RunId} vs {after.RunId}");
        text.AppendLine();
        text.AppendLine(
            $"Te same dane na świeżych bazach, te same przypadki; wynik z celu propozycji (zdarzenie `confirmation`), nie z tekstu. Prompt: `{before.Model}` → `{after.Model}`. Miarodajne dla tych przypadków, nie dla całego agenta."
        );
        text.AppendLine();
        text.AppendLine(
            $"| # | Przypadek | Wiadomość | Oczekiwane | {before.RunId} | {after.RunId} |"
        );
        text.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var b in before.Results)
        {
            var a = after.Results.Single(r => r.Case == b.Case);
            text.AppendLine(
                $"| {b.Case} | {b.Rule} | „{b.Message}” | {Names(b.Expected)} | {Names(b.Proposals)} {(b.Ok ? "✓" : "✗")} | {Names(a.Proposals)} {(a.Ok ? "✓" : "✗")} |"
            );
        }
        text.AppendLine();
        text.AppendLine(
            $"- {before.RunId}: {before.Results.Count(r => r.Ok)}/{before.Results.Count}; {after.RunId}: {after.Results.Count(r => r.Ok)}/{after.Results.Count}."
        );
        return text.ToString();
    }

    private static string Names(IReadOnlyCollection<string> names) =>
        names.Count == 0 ? "brak propozycji" : string.Join(", ", names);

    private static async Task<TranscriptTurn> AskAsync(
        ApiSession session,
        string message,
        CancellationToken ct
    )
    {
        // A new conversation per case: no history carries over.
        using var started = await session.PostAsync(
            "/api/chat/conversations",
            null,
            ct,
            Guid.NewGuid().ToString()
        );
        await ApiSession.EnsureAsync(started, "start conversation", ct);
        var id = (await started.Content.ReadFromJsonAsync<JsonElement>(ApiSession.JsonOptions, ct))
            .GetProperty("id")
            .GetGuid();
        var turn = await ChatScenarios.TurnAsync(session, id, message, [], ct);
        using var ended = await session.PostAsync($"/api/chat/conversations/{id}/end", null, ct);
        await ApiSession.EnsureAsync(ended, "end conversation", ct);
        return turn;
    }

    private static async Task<ApiSession> AccountAsync(
        string api,
        string run,
        string account,
        string secret,
        ApiSession cashier,
        CancellationToken ct
    )
    {
        var email = $"cmp-{run}-{account}@{Population.EmailDomain}";
        var password = Population.PasswordFor(secret, email);
        var session = new ApiSession(api);
        using (
            var registered = await session.PostAsync(
                "/api/auth/register",
                new
                {
                    email,
                    password,
                    displayName = $"Cmp {account}",
                },
                ct
            )
        )
            await ApiSession.EnsureAsync(registered, "register", ct);
        await LoginAsync(session, email, password, ct);
        using (
            var card = await session.PostAsync(
                "/api/me/loyalty-card",
                null,
                ct,
                Guid.NewGuid().ToString()
            )
        )
            await ApiSession.EnsureAsync(card, "card", ct);
        // Kept in memory only.
        var code = (await session.GetAsync<JsonElement>("/api/me/loyalty-card", ct))
            .GetProperty("code")
            .GetString();
        var today = DateOnly.FromDateTime(
            TimeZoneInfo
                .ConvertTime(
                    DateTimeOffset.UtcNow,
                    TimeZoneInfo.FindSystemTimeZoneById("Europe/Warsaw")
                )
                .DateTime
        );
        using var purchase = await cashier.PostAsync(
            "/api/cashier/fuel-purchases",
            new
            {
                cardCode = code,
                amount = 500m,
                liters = 72.57m,
                saleDate = today.ToString("yyyy-MM-dd"),
                receiptNumber = $"CMP-{Guid.NewGuid():N}"[..24],
                couponCode = (string?)null,
            },
            ct,
            Guid.NewGuid().ToString()
        );
        await ApiSession.EnsureAsync(purchase, "purchase", ct);
        return session;
    }

    private static async Task<ApiSession> SignInAsync(
        string api,
        string email,
        string password,
        CancellationToken ct
    )
    {
        var session = new ApiSession(api);
        await LoginAsync(session, email, password, ct);
        return session;
    }

    private static async Task LoginAsync(
        ApiSession session,
        string email,
        string password,
        CancellationToken ct
    )
    {
        using var response = await session.PostAsync(
            "/api/auth/login",
            new { email, password },
            ct
        );
        await ApiSession.EnsureAsync(response, "login", ct);
    }
}
