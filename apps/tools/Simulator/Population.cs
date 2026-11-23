using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace FuelClub.Simulator;

/// <summary>
/// Creates the run's customers through the public API only: registration, card, purchases at the
/// cashier, exchanges, activation and redemption. The distribution follows Profile.
/// </summary>
public sealed class Population(Manifest manifest, Settings settings)
{
    public const string EmailDomain = "sim.fuelclub.test";
    private const int DiscountCost = 200;
    private const int ProductCost = 100;

    private readonly Pacer _auth = new(settings.AuthPerMinute);
    private readonly Pacer _purchases = new(settings.PurchasesPerMinute);

    public async Task RunAsync(int count, CancellationToken ct)
    {
        for (var i = manifest.Accounts.Count; i < count; i++)
            manifest.Accounts.Add(
                new SimAccount
                {
                    Index = i,
                    Email = $"sim-{manifest.RunId}-{i:0000}@{EmailDomain}",
                    Profile = (Profile)(i % 4),
                }
            );
        manifest.Save();

        using var cashier = await SignInAsync(settings.CashierEmail, settings.CashierPassword, ct);
        using var admin = await SignInAsync(settings.AdminEmail, settings.AdminPassword, ct);
        var discount = await OfferAsync(admin, "discount", ct);
        var product = await OfferAsync(admin, "product", ct);

        var failures = 0;
        await Parallel.ForEachAsync(
            manifest.Accounts.Take(count),
            new ParallelOptions
            {
                MaxDegreeOfParallelism = settings.Parallel,
                CancellationToken = ct,
            },
            async (account, token) =>
            {
                try
                {
                    await AccountAsync(account, cashier, discount, product, token);
                    Console.WriteLine($"{account.Email}: {account.Profile} ok");
                }
                catch (ApiException e)
                {
                    Interlocked.Increment(ref failures);
                    Console.WriteLine($"{account.Email}: {e.Message}");
                }
            }
        );
        var done = manifest.Operations.Count(o => o.Status == OperationStatus.Done);
        Console.WriteLine(
            $"run {manifest.RunId}: {count} accounts, {done}/{manifest.Operations.Count} operations done, {failures} accounts failed"
        );
        if (failures > 0)
            Environment.ExitCode = 1;
    }

    private async Task AccountAsync(
        SimAccount account,
        ApiSession cashier,
        Offer discount,
        Offer product,
        CancellationToken ct
    )
    {
        var name = $"a{account.Index:0000}";
        var password = Password(account.Email);
        using var customer = new ApiSession(manifest.ApiUrl);

        await Step(
            $"{name}:register",
            "register",
            name,
            async op =>
            {
                await _auth.WaitAsync(ct);
                using var response = await customer.PostAsync(
                    "/api/auth/register",
                    new
                    {
                        email = account.Email,
                        password,
                        displayName = $"Sim {account.Index}",
                    },
                    ct
                );
                // A rerun after an unknown outcome: the account may exist already; login proves it.
                if (!response.IsSuccessStatusCode && response.StatusCode != HttpStatusCode.Conflict)
                    await ApiSession.EnsureAsync(response, "register", ct);
                return null;
            }
        );
        await _auth.WaitAsync(ct);
        await LoginAsync(customer, account.Email, password, ct);
        var me = await customer.GetAsync<JsonElement>("/api/auth/me", ct);
        account.UserId = me.GetProperty("id").GetString();

        await Step(
            $"{name}:card",
            "card",
            name,
            async op =>
            {
                using var response = await customer.PostAsync(
                    "/api/me/loyalty-card",
                    null,
                    ct,
                    op.IdempotencyKey
                );
                await ApiSession.EnsureAsync(response, "card", ct);
                return null;
            }
        );
        // Kept in memory only: the card code says whose points these are.
        var card = (await customer.GetAsync<JsonElement>("/api/me/loyalty-card", ct))
            .GetProperty("code")
            .GetString()!;

        decimal[] purchases = account.Profile switch
        {
            Profile.Zero => [],
            Profile.Low => [80m],
            Profile.Medium => [300m, 320m],
            _ => [1500m, 1800m, 1200m],
        };
        for (var n = 0; n < purchases.Length; n++)
            await PurchaseAsync(
                cashier,
                card,
                $"{name}:purchase:{n}",
                name,
                purchases[n],
                null,
                ct
            );

        if (account.Profile is Profile.Medium or Profile.High)
        {
            await ExchangeAsync(customer, discount, $"{name}:exchange:0", name, ct);
        }
        if (account.Profile is Profile.High)
        {
            // Redeemed: a discount coupon used with a purchase.
            var used = await ExchangeAsync(customer, discount, $"{name}:exchange:1", name, ct);
            await ActivateAsync(customer, used, $"{name}:activate:1", name, ct);
            await Step(
                $"{name}:redeem:1",
                "redeem",
                name,
                async op =>
                {
                    var code = await CouponCodeAsync(customer, used, ct);
                    return await PurchaseCallAsync(cashier, card, op, 250m, code, ct);
                }
            );
            // Active: a product reward waiting for pickup; Purchased: one more.
            var waiting = await ExchangeAsync(customer, product, $"{name}:exchange:2", name, ct);
            await ActivateAsync(customer, waiting, $"{name}:activate:2", name, ct);
            await ExchangeAsync(customer, product, $"{name}:exchange:3", name, ct);
        }
    }

    private Task PurchaseAsync(
        ApiSession cashier,
        string card,
        string key,
        string name,
        decimal amount,
        string? coupon,
        CancellationToken ct
    ) =>
        Step(key, "purchase", name, op => PurchaseCallAsync(cashier, card, op, amount, coupon, ct));

    private async Task<string?> PurchaseCallAsync(
        ApiSession cashier,
        string card,
        Operation op,
        decimal amount,
        string? coupon,
        CancellationToken ct
    )
    {
        await _purchases.WaitAsync(ct);
        // The same body on a rerun: the key stays, so must the receipt and the date.
        using var response = await cashier.PostAsync(
            "/api/cashier/fuel-purchases",
            new
            {
                cardCode = card,
                amount,
                liters = Math.Round(amount / 6.89m, 2),
                saleDate = manifest.CreatedAt.ToString("yyyy-MM-dd"),
                receiptNumber = Receipt(op.Key),
                couponCode = coupon,
            },
            ct,
            op.IdempotencyKey
        );
        await ApiSession.EnsureAsync(response, op.Key, ct);
        return (await response.Content.ReadFromJsonAsync<JsonElement>(ct))
            .GetProperty("purchaseId")
            .GetString();
    }

    private async Task<string> ExchangeAsync(
        ApiSession customer,
        Offer offer,
        string key,
        string name,
        CancellationToken ct
    ) =>
        (
            await Step(
                key,
                "exchange",
                name,
                async op =>
                {
                    using var response = await customer.PostAsync(
                        $"/api/rewards/{offer.Id}/exchange",
                        new { version = offer.Version },
                        ct,
                        op.IdempotencyKey
                    );
                    await ApiSession.EnsureAsync(response, key, ct);
                    return (await response.Content.ReadFromJsonAsync<JsonElement>(ct))
                        .GetProperty("coupon")
                        .GetProperty("id")
                        .GetString();
                }
            )
        )!;

    private Task ActivateAsync(
        ApiSession customer,
        string coupon,
        string key,
        string name,
        CancellationToken ct
    ) =>
        Step(
            key,
            "activate",
            name,
            async op =>
            {
                using var response = await customer.PostAsync(
                    $"/api/me/coupons/{coupon}/activate",
                    null,
                    ct,
                    op.IdempotencyKey
                );
                await ApiSession.EnsureAsync(response, key, ct);
                return null;
            }
        );

    /// <summary>Read when needed, never written down (a redemption code is a secret).</summary>
    private static async Task<string> CouponCodeAsync(
        ApiSession customer,
        string coupon,
        CancellationToken ct
    ) =>
        (await customer.GetAsync<JsonElement>($"/api/me/coupons/{coupon}", ct))
            .GetProperty("code")
            .GetString()
        ?? throw new InvalidOperationException($"Coupon {coupon} has no code.");

    private async Task<Offer> OfferAsync(ApiSession admin, string kind, CancellationToken ct)
    {
        var id = await Step(
            $"offer:{kind}",
            "offer",
            "admin",
            async op =>
            {
                using var response = await admin.PostAsync(
                    "/api/admin/rewards",
                    kind == "discount"
                        ? new
                        {
                            name = $"SIM {manifest.RunId} rabat",
                            description = "Oferta symulacji",
                            pointsCost = DiscountCost,
                            kind = "FuelDiscount",
                            discountPerLiter = (decimal?)0.15m,
                            maxLiters = (decimal?)40m,
                            isActive = true,
                        }
                        : new
                        {
                            name = $"SIM {manifest.RunId} hot dog",
                            description = "Nagroda symulacji",
                            pointsCost = ProductCost,
                            kind = "Product",
                            discountPerLiter = (decimal?)null,
                            maxLiters = (decimal?)null,
                            isActive = true,
                        },
                    ct,
                    op.IdempotencyKey
                );
                await ApiSession.EnsureAsync(response, $"offer {kind}", ct);
                return (await response.Content.ReadFromJsonAsync<JsonElement>(ct))
                    .GetProperty("id")
                    .GetString();
            }
        );
        var offerId = Guid.Parse(id!);
        lock (manifest.OfferIds)
            if (!manifest.OfferIds.Contains(offerId))
                manifest.OfferIds.Add(offerId);
        manifest.Save();
        var catalog = await admin.GetAsync<JsonElement>("/api/admin/rewards", ct);
        var offer = catalog
            .GetProperty("items")
            .EnumerateArray()
            .Single(o => o.GetProperty("id").GetGuid() == offerId);
        return new Offer(offerId, offer.GetProperty("version").GetInt32());
    }

    private async Task<ApiSession> SignInAsync(string email, string password, CancellationToken ct)
    {
        var session = new ApiSession(manifest.ApiUrl);
        await _auth.WaitAsync(ct);
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
        await ApiSession.EnsureAsync(response, $"login {email}", ct);
    }

    /// <summary>Done is skipped; pending or failed runs again with the same idempotency key.</summary>
    private async Task<string?> Step(
        string key,
        string kind,
        string account,
        Func<Operation, Task<string?>> action
    )
    {
        var op = manifest.Operation(key, kind, account);
        if (op.Status == OperationStatus.Done)
            return op.Result;
        op.Status = OperationStatus.Pending;
        manifest.Save();
        try
        {
            op.Result = await action(op);
            op.Status = OperationStatus.Done;
            op.Error = null;
            return op.Result;
        }
        catch (ApiException e)
        {
            op.Status = OperationStatus.Failed;
            op.Error = $"{(int)e.Status} {e.Code}";
            throw;
        }
        finally
        {
            manifest.Save();
        }
    }

    /// <summary>From the run secret: the same password on every rerun, nowhere on disk.</summary>
    private string Password(string email)
    {
        var hash = HMACSHA256.HashData(
            Encoding.UTF8.GetBytes(settings.Secret),
            Encoding.UTF8.GetBytes(email)
        );
        return Convert.ToBase64String(hash)[..20].Replace('+', 'x').Replace('/', 'y') + "aA1!";
    }

    private string Receipt(string key) =>
        $"SIM-{Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(manifest.RunId + key)))[..20]}";

    private sealed record Offer(Guid Id, int Version);
}
