using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class RewardExchangeTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private const string Coupons = "/api/me/coupons";
    private readonly List<string> _emails = [];
    private readonly string _tag = Guid.NewGuid().ToString("N")[..8];

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        foreach (var email in _emails)
            await factory.DeleteTestUserAsync(email);
        await DbAsync(db =>
            db.RewardOffers.Where(offer => offer.Name.Contains(_tag)).ExecuteDeleteAsync()
        );
    }

    private sealed record Customer(HttpClient Client, string UserId);

    private sealed record CouponBody(
        Guid Id,
        Guid OfferId,
        string OfferName,
        int PointsCost,
        decimal DiscountPerLiter,
        decimal MaxLiters,
        decimal? MinLiters,
        string Status,
        DateTimeOffset PurchasedAt,
        DateTimeOffset? ActivatedAt,
        DateTimeOffset? RedeemedAt
    );

    private sealed record ExchangeBody(CouponBody Coupon, int Balance);

    private sealed record ListBody<T>(List<T> Items);

    private sealed record CatalogItem(Guid Id, string Name, int PointsCost, int Version);

    private sealed record CouponExchangeBody(Guid CouponId, string OfferName);

    private sealed record TransactionBody(
        Guid Id,
        string Type,
        int Delta,
        int BalanceAfter,
        object? FuelPurchase,
        CouponExchangeBody? CouponExchange
    );

    private sealed record ProblemBody(string? Code);

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<(HttpClient Client, string UserId)> AccountAsync(string role)
    {
        var email = $"exchange-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, role);
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, Password);
        var userId = await DbAsync(db =>
            db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync()
        );
        return (client, userId);
    }

    private async Task<Customer> CustomerAsync(int? balance)
    {
        var (client, userId) = await AccountAsync(RoleNames.User);
        if (balance is { } points)
            await DbAsync(async db =>
            {
                db.LoyaltyAccounts.Add(new LoyaltyAccount { UserId = userId, Balance = points });
                return await db.SaveChangesAsync();
            });
        return new Customer(client, userId);
    }

    private async Task<RewardOffer> OfferAsync(
        int cost = 500,
        bool active = true,
        decimal rate = 0.20m,
        decimal liters = 50m,
        decimal? minLiters = null
    )
    {
        var offer = new RewardOffer
        {
            Id = Guid.CreateVersion7(),
            Name = $"Rabat {cost} {_tag}",
            Description = "Na dowolne paliwo",
            PointsCost = cost,
            DiscountPerLiter = rate,
            MaxLiters = liters,
            MinLiters = minLiters,
            IsActive = active,
        };
        await DbAsync(async db =>
        {
            db.RewardOffers.Add(offer);
            return await db.SaveChangesAsync();
        });
        return offer;
    }

    private static string NewKey() => Guid.NewGuid().ToString();

    private static async Task<HttpResponseMessage> ExchangeAsync(
        HttpClient client,
        Guid offerId,
        int? version,
        string? key = null,
        bool csrf = true
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/rewards/{offerId}/exchange"
        )
        {
            Content = JsonContent.Create(new { version }),
        };
        request.Headers.Add(Idempotency.HeaderName, key ?? NewKey());
        if (csrf)
            request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        return await client.SendAsync(request);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>())!.Code;

    private Task<int?> BalanceAsync(string userId) =>
        DbAsync(db =>
            db.LoyaltyAccounts.Where(a => a.UserId == userId)
                .Select(a => (int?)a.Balance)
                .SingleOrDefaultAsync()
        );

    private Task<int> CouponCountAsync(string userId) =>
        DbAsync(db => db.Coupons.CountAsync(c => c.UserId == userId));

    private Task<int> ExchangeEntriesAsync(string userId) =>
        DbAsync(db =>
            db.PointsLedgerEntries.CountAsync(e =>
                e.UserId == userId && e.Type == PointsLedgerEntryType.CouponExchange
            )
        );

    private async Task<HttpResponseMessage> EditOfferAsync(
        HttpClient admin,
        RewardOffer offer,
        int version,
        int cost
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Put, $"/api/admin/rewards/{offer.Id}")
        {
            Content = JsonContent.Create(
                new
                {
                    name = offer.Name,
                    description = offer.Description,
                    pointsCost = cost,
                    discountPerLiter = offer.DiscountPerLiter,
                    maxLiters = offer.MaxLiters,
                    isActive = true,
                    version,
                }
            ),
        };
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await admin.GetCsrfTokenAsync());
        return await admin.SendAsync(request);
    }

    [Fact]
    public async Task Exchange_buys_a_coupon_charges_the_points_once_and_appears_in_history()
    {
        var customer = await CustomerAsync(1200);
        var offer = await OfferAsync(cost: 500, rate: 0.25m, liters: 40.5m);

        var catalog = (
            await customer.Client.GetFromJsonAsync<ListBody<CatalogItem>>("/api/rewards")
        )!.Items.Single(i => i.Id == offer.Id);
        Assert.Equal(1, catalog.Version);

        var response = await ExchangeAsync(customer.Client, offer.Id, catalog.Version);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ExchangeBody>())!;
        Assert.Equal(700, body.Balance);
        Assert.Equal(
            (offer.Id, offer.Name, 500, 0.25m, 40.5m, "Purchased"),
            (
                body.Coupon.OfferId,
                body.Coupon.OfferName,
                body.Coupon.PointsCost,
                body.Coupon.DiscountPerLiter,
                body.Coupon.MaxLiters,
                body.Coupon.Status
            )
        );
        Assert.Null(body.Coupon.ActivatedAt);
        Assert.Equal($"{Coupons}/{body.Coupon.Id}", response.Headers.Location!.OriginalString);

        Assert.Equal(
            body.Coupon,
            await customer.Client.GetFromJsonAsync<CouponBody>(
                response.Headers.Location.OriginalString
            )
        );
        Assert.Equal(
            [body.Coupon.Id],
            (await customer.Client.GetFromJsonAsync<ListBody<CouponBody>>(Coupons))!.Items.Select(
                c => c.Id
            )
        );
        Assert.Equal(700, await BalanceAsync(customer.UserId));
        Assert.Null(
            await DbAsync(db =>
                db.Coupons.Where(c => c.Id == body.Coupon.Id).Select(c => c.Code).SingleAsync()
            )
        );

        var history = (
            await customer.Client.GetFromJsonAsync<ListBody<TransactionBody>>(
                "/api/me/transactions"
            )
        )!.Items;
        var entry = Assert.Single(history);
        Assert.Equal(("CouponExchange", -500, 700), (entry.Type, entry.Delta, entry.BalanceAfter));
        Assert.Equal(new CouponExchangeBody(body.Coupon.Id, offer.Name), entry.CouponExchange);
        Assert.Null(entry.FuelPurchase);
    }

    [Fact]
    public async Task The_coupon_keeps_the_minimum_liters_of_the_offer_after_an_edit()
    {
        var customer = await CustomerAsync(1200);
        var offer = await OfferAsync(cost: 500, liters: 20m, minLiters: 30m);

        var response = await ExchangeAsync(customer.Client, offer.Id, 1);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<ExchangeBody>())!;
        Assert.Equal(30m, body.Coupon.MinLiters);
        await DbAsync(db =>
            db.RewardOffers.Where(o => o.Id == offer.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(o => o.MinLiters, (decimal?)null))
        );
        Assert.Equal(
            30m,
            await DbAsync(db =>
                db.Coupons.Where(c => c.Id == body.Coupon.Id).Select(c => c.MinLiters).SingleAsync()
            )
        );
    }

    [Fact]
    public async Task The_whole_balance_can_be_spent()
    {
        var customer = await CustomerAsync(500);
        var offer = await OfferAsync(cost: 500);
        var response = await ExchangeAsync(customer.Client, offer.Id, 1);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.Equal(0, (await response.Content.ReadFromJsonAsync<ExchangeBody>())!.Balance);
    }

    [Theory]
    [InlineData(499)]
    [InlineData(null)]
    public async Task Not_enough_points_changes_nothing(int? balance)
    {
        var customer = await CustomerAsync(balance);
        var offer = await OfferAsync(cost: 500);

        var response = await ExchangeAsync(customer.Client, offer.Id, 1);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("rewards.insufficient_points", await CodeOf(response));
        Assert.Equal(balance, await BalanceAsync(customer.UserId));
        Assert.Equal(0, await CouponCountAsync(customer.UserId));
        Assert.Equal(0, await ExchangeEntriesAsync(customer.UserId));
    }

    [Fact]
    public async Task Offer_checks_come_in_order_missing_inactive_changed()
    {
        var customer = await CustomerAsync(10_000);

        var missing = await ExchangeAsync(customer.Client, Guid.NewGuid(), 1);
        Assert.Equal(
            (HttpStatusCode.NotFound, "rewards.offer_not_found"),
            (missing.StatusCode, await CodeOf(missing))
        );

        // Deactivation also raises the version: the customer must learn "unavailable", not "changed".
        var inactive = await OfferAsync(active: false);
        await DbAsync(db =>
            db.RewardOffers.Where(o => o.Id == inactive.Id)
                .ExecuteUpdateAsync(s => s.SetProperty(o => o.Version, 2))
        );
        var unavailable = await ExchangeAsync(customer.Client, inactive.Id, 1);
        Assert.Equal(
            (HttpStatusCode.Conflict, "rewards.offer_inactive"),
            (unavailable.StatusCode, await CodeOf(unavailable))
        );

        var offer = await OfferAsync();
        await DbAsync(async db =>
        {
            var tracked = await db.RewardOffers.SingleAsync(o => o.Id == offer.Id);
            tracked.PointsCost = 900;
            return await db.SaveChangesAsync();
        });
        var changed = await ExchangeAsync(customer.Client, offer.Id, 1);
        Assert.Equal(
            (HttpStatusCode.Conflict, "rewards.offer_changed"),
            (changed.StatusCode, await CodeOf(changed))
        );

        Assert.Equal(10_000, await BalanceAsync(customer.UserId));
        Assert.Equal(0, await CouponCountAsync(customer.UserId));
        Assert.Equal(
            HttpStatusCode.Created,
            (await ExchangeAsync(customer.Client, offer.Id, 2)).StatusCode
        );
        Assert.Equal(9_100, await BalanceAsync(customer.UserId));
    }

    [Theory]
    [InlineData(null, "rewards.required_field")]
    [InlineData(0, "rewards.version_out_of_range")]
    public async Task Version_is_required(int? version, string code)
    {
        var customer = await CustomerAsync(1000);
        var offer = await OfferAsync();
        var response = await ExchangeAsync(customer.Client, offer.Id, version);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("rewards.validation_failed", await CodeOf(response));
        Assert.Contains($"\"{code}\"", await response.Content.ReadAsStringAsync());
        Assert.Equal(1000, await BalanceAsync(customer.UserId));
    }

    [Fact]
    public async Task A_retry_with_the_same_key_returns_the_same_coupon_without_charging_again()
    {
        var customer = await CustomerAsync(2000);
        var offer = await OfferAsync(cost: 500);
        var key = NewKey();

        var first = await ExchangeAsync(customer.Client, offer.Id, 1, key);
        var retry = await ExchangeAsync(customer.Client, offer.Id, 1, key);

        Assert.Equal(HttpStatusCode.Created, retry.StatusCode);
        Assert.Equal(
            (await first.Content.ReadFromJsonAsync<ExchangeBody>())!,
            (await retry.Content.ReadFromJsonAsync<ExchangeBody>())!
        );
        Assert.Equal(1500, await BalanceAsync(customer.UserId));
        Assert.Equal(1, await CouponCountAsync(customer.UserId));
    }

    [Fact]
    public async Task Parallel_requests_with_the_same_key_buy_one_coupon()
    {
        var customer = await CustomerAsync(5000);
        var offer = await OfferAsync(cost: 500);
        var key = NewKey();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => ExchangeAsync(customer.Client, offer.Id, 1, key))
        );

        Assert.Contains(responses, r => r.StatusCode == HttpStatusCode.Created);
        Assert.All(
            responses,
            r =>
                Assert.Contains(
                    r.StatusCode,
                    new[] { HttpStatusCode.Created, HttpStatusCode.Conflict }
                )
        );
        Assert.Equal(1, await CouponCountAsync(customer.UserId));
        Assert.Equal(4500, await BalanceAsync(customer.UserId));
    }

    [Fact]
    public async Task Parallel_exchanges_never_overspend()
    {
        var customer = await CustomerAsync(1000);
        var offer = await OfferAsync(cost: 500);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => ExchangeAsync(customer.Client, offer.Id, 1))
        );

        Assert.Equal(2, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
        var rejected = responses.Where(r => r.StatusCode != HttpStatusCode.Created).ToList();
        Assert.Equal(4, rejected.Count);
        foreach (var response in rejected)
            Assert.Equal("rewards.insufficient_points", await CodeOf(response));
        Assert.Equal(0, await BalanceAsync(customer.UserId));
        Assert.Equal(2, await CouponCountAsync(customer.UserId));
        Assert.Equal(2, await ExchangeEntriesAsync(customer.UserId));
        var balancesAfter = await DbAsync(db =>
            db.PointsLedgerEntries.Where(e => e.UserId == customer.UserId)
                .OrderBy(e => e.Sequence)
                .Select(e => e.BalanceAfter)
                .ToListAsync()
        );
        Assert.Equal([500, 0], balancesAfter);
    }

    [Fact]
    public async Task An_admin_edit_waits_for_a_running_exchange_and_the_coupon_keeps_the_terms_the_customer_saw()
    {
        var customer = await CustomerAsync(2000);
        var (admin, _) = await AccountAsync(RoleNames.Admin);
        var offer = await OfferAsync(cost: 500);

        Task<HttpResponseMessage> exchange,
            edit;
        using (var gate = factory.LockGate.Arm(CustomerLock.Key(customer.UserId)))
        {
            // The exchange holds the offer row (FOR SHARE) and is paused right after the customer lock.
            exchange = ExchangeAsync(customer.Client, offer.Id, 1);
            await gate.Acquired.WaitAsync(TimeSpan.FromSeconds(5));

            edit = EditOfferAsync(admin, offer, 1, 900);
            await WaitForRowLockWaiterAsync();
            Assert.False(edit.IsCompleted);

            gate.Release();
        }

        var exchanged = await exchange;
        Assert.Equal(HttpStatusCode.Created, exchanged.StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await edit).StatusCode);
        var coupon = (await exchanged.Content.ReadFromJsonAsync<ExchangeBody>())!.Coupon;
        Assert.Equal(500, coupon.PointsCost);
        Assert.Equal(1500, await BalanceAsync(customer.UserId));

        // The coupon is a snapshot: the later edit does not change it.
        Assert.Equal(
            500,
            (
                await customer.Client.GetFromJsonAsync<CouponBody>($"{Coupons}/{coupon.Id}")
            )!.PointsCost
        );
        var stale = await ExchangeAsync(customer.Client, offer.Id, 1);
        Assert.Equal("rewards.offer_changed", await CodeOf(stale));
    }

    [Fact]
    public async Task A_busy_customer_gets_503_nothing_is_charged_and_the_same_key_works_later()
    {
        var customer = await CustomerAsync(1000);
        var offer = await OfferAsync(cost: 500);
        var key = NewKey();

        HttpResponseMessage busy;
        await using (
            var connection = new NpgsqlConnection(
                factory
                    .Services.GetRequiredService<IConfiguration>()
                    .GetConnectionString("DefaultConnection")
            )
        )
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (
                var command = new NpgsqlCommand(
                    "SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))",
                    connection,
                    transaction
                )
            )
            {
                command.Parameters.AddWithValue("key", CustomerLock.Key(customer.UserId));
                await command.ExecuteNonQueryAsync();
            }
            busy = await ExchangeAsync(customer.Client, offer.Id, 1, key);
            await transaction.RollbackAsync();
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
        Assert.Equal("loyalty.customer_busy", await CodeOf(busy));
        Assert.NotNull(busy.Headers.RetryAfter);
        Assert.Equal(1000, await BalanceAsync(customer.UserId));
        Assert.Equal(0, await CouponCountAsync(customer.UserId));

        Assert.Equal(
            HttpStatusCode.Created,
            (await ExchangeAsync(customer.Client, offer.Id, 1, key)).StatusCode
        );
        Assert.Equal(500, await BalanceAsync(customer.UserId));
    }

    [Fact]
    public async Task An_offer_locked_too_long_gives_503_nothing_is_charged_and_the_same_key_works_later()
    {
        var customer = await CustomerAsync(1000);
        var offer = await OfferAsync(cost: 500);
        var key = NewKey();

        HttpResponseMessage busy;
        await using (
            var connection = new NpgsqlConnection(
                factory
                    .Services.GetRequiredService<IConfiguration>()
                    .GetConnectionString("DefaultConnection")
            )
        )
        {
            await connection.OpenAsync();
            await using var transaction = await connection.BeginTransactionAsync();
            await using (
                var command = new NpgsqlCommand(
                    """SELECT 1 FROM "RewardOffers" WHERE "Id" = @id FOR UPDATE""",
                    connection,
                    transaction
                )
            )
            {
                command.Parameters.AddWithValue("id", offer.Id);
                await command.ExecuteNonQueryAsync();
            }
            busy = await ExchangeAsync(customer.Client, offer.Id, 1, key);
            await transaction.RollbackAsync();
        }

        Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
        Assert.Equal("rewards.offer_busy", await CodeOf(busy));
        Assert.NotNull(busy.Headers.RetryAfter);
        Assert.Equal(1000, await BalanceAsync(customer.UserId));
        Assert.Equal(0, await CouponCountAsync(customer.UserId));

        Assert.Equal(
            HttpStatusCode.Created,
            (await ExchangeAsync(customer.Client, offer.Id, 1, key)).StatusCode
        );
        Assert.Equal(500, await BalanceAsync(customer.UserId));
    }

    [Theory]
    [InlineData("00:00:00", false)]
    [InlineData("-00:00:01", false)]
    [InlineData("00:00:00.0001", false)]
    [InlineData("00:00:30.001", false)]
    [InlineData("00:00:00.001", true)]
    [InlineData("00:00:30", true)]
    public void Offer_lock_timeout_must_be_between_1_ms_and_30_s(string value, bool valid) =>
        Assert.Equal(
            valid,
            new FuelClub.Api.Modules.Rewards.Infrastructure.RewardsOptionsValidator()
                .Validate(
                    null,
                    new FuelClub.Api.Modules.Rewards.Infrastructure.RewardsOptions
                    {
                        OfferLockTimeout = TimeSpan.Parse(
                            value,
                            System.Globalization.CultureInfo.InvariantCulture
                        ),
                    }
                )
                .Succeeded
        );

    [Fact]
    public async Task Coupons_are_visible_only_to_their_owner()
    {
        var owner = await CustomerAsync(1000);
        var other = await CustomerAsync(1000);
        var offer = await OfferAsync(cost: 500);
        var coupon = (
            await (
                await ExchangeAsync(owner.Client, offer.Id, 1)
            ).Content.ReadFromJsonAsync<ExchangeBody>()
        )!.Coupon;

        var foreign = await other.Client.GetAsync($"{Coupons}/{coupon.Id}");
        Assert.Equal(
            (HttpStatusCode.NotFound, "rewards.coupon_not_found"),
            (foreign.StatusCode, await CodeOf(foreign))
        );
        Assert.Empty((await other.Client.GetFromJsonAsync<ListBody<CouponBody>>(Coupons))!.Items);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await owner.Client.GetAsync($"{Coupons}/{Guid.NewGuid()}")).StatusCode
        );
    }

    [Fact]
    public async Task Exchange_and_coupons_require_a_customer_csrf_and_a_key()
    {
        var offer = await OfferAsync(cost: 500);
        var anonymous = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(Coupons)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (
                await anonymous.PostAsync(
                    $"/api/rewards/{offer.Id}/exchange",
                    JsonContent.Create(new { version = 1 })
                )
            ).StatusCode
        );

        var (cashier, _) = await AccountAsync(RoleNames.Cashier);
        Assert.Equal(HttpStatusCode.Forbidden, (await cashier.GetAsync(Coupons)).StatusCode);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await ExchangeAsync(cashier, offer.Id, 1)).StatusCode
        );

        var customer = await CustomerAsync(1000);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await ExchangeAsync(customer.Client, offer.Id, 1, csrf: false)).StatusCode
        );
        using var noKey = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/rewards/{offer.Id}/exchange"
        )
        {
            Content = JsonContent.Create(new { version = 1 }),
        };
        noKey.Headers.Add(TestHttpExtensions.CsrfHeader, await customer.Client.GetCsrfTokenAsync());
        Assert.Equal(
            HttpStatusCode.BadRequest,
            (await customer.Client.SendAsync(noKey)).StatusCode
        );
        Assert.Equal(1000, await BalanceAsync(customer.UserId));
    }

    private async Task WaitForRowLockWaiterAsync()
    {
        for (var attempt = 0; attempt < 500; attempt++)
        {
            var waiting = await DbAsync(db =>
                db.Database.SqlQuery<int>(
                        $"""SELECT count(*)::int AS "Value" FROM pg_locks WHERE locktype <> 'advisory' AND NOT granted"""
                    )
                    .SingleAsync()
            );
            if (waiting > 0)
                return;
            await Task.Delay(10);
        }
        throw new TimeoutException("Expected the admin edit to wait for the offer row.");
    }
}
