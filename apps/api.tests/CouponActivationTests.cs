using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class CouponActivationTests(ApiFactory factory) : IAsyncLifetime
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
        string Status,
        string? Code,
        int PointsCost,
        DateTimeOffset PurchasedAt,
        DateTimeOffset? ActivatedAt,
        DateTimeOffset? RedeemedAt
    );

    private sealed record ExchangeBody(CouponBody Coupon, int Balance);

    private sealed record ListBody<T>(List<T> Items);

    private sealed record ProblemBody(string? Code);

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<Customer> AccountAsync(
        string role = RoleNames.User,
        int? balance = 1000,
        WebApplicationFactory<Program>? host = null
    )
    {
        var email = $"activation-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        await factory.CreateTestUserAsync(email, Password, role);
        var client = (host ?? factory).CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, Password);
        var userId = await DbAsync(db =>
            db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync()
        );
        if (balance is { } points)
            await DbAsync(async db =>
            {
                db.LoyaltyAccounts.Add(new LoyaltyAccount { UserId = userId, Balance = points });
                return await db.SaveChangesAsync();
            });
        return new Customer(client, userId);
    }

    /// <summary>A purchased coupon, bought through the exchange endpoint.</summary>
    private async Task<CouponBody> CouponAsync(Customer customer, bool deactivateOffer = false)
    {
        var offer = new RewardOffer
        {
            Id = Guid.CreateVersion7(),
            Name = $"Rabat {_tag}",
            Description = "Na dowolne paliwo",
            PointsCost = 500,
            DiscountPerLiter = 0.20m,
            MaxLiters = 50m,
            IsActive = true,
        };
        await DbAsync(async db =>
        {
            db.RewardOffers.Add(offer);
            return await db.SaveChangesAsync();
        });

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"/api/rewards/{offer.Id}/exchange"
        )
        {
            Content = JsonContent.Create(new { version = 1 }),
        };
        request.Headers.Add(Idempotency.HeaderName, NewKey());
        request.Headers.Add(
            TestHttpExtensions.CsrfHeader,
            await customer.Client.GetCsrfTokenAsync()
        );
        var response = await customer.Client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        if (deactivateOffer)
            await DbAsync(db =>
                db.RewardOffers.Where(o => o.Id == offer.Id)
                    .ExecuteUpdateAsync(set =>
                        set.SetProperty(o => o.IsActive, false)
                            .SetProperty(o => o.Version, o => o.Version + 1)
                    )
            );
        return (await response.Content.ReadFromJsonAsync<ExchangeBody>())!.Coupon;
    }

    private static string NewKey() => Guid.NewGuid().ToString();

    private static async Task<HttpResponseMessage> ActivateAsync(
        HttpClient client,
        Guid couponId,
        string? key = null,
        bool csrf = true,
        bool withKey = true
    )
    {
        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            $"{Coupons}/{couponId}/activate"
        );
        if (withKey)
            request.Headers.Add(Idempotency.HeaderName, key ?? NewKey());
        if (csrf)
            request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        return await client.SendAsync(request);
    }

    private static async Task<CouponBody> BodyOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<CouponBody>())!;

    private static async Task<string?> ErrorOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>())!.Code;

    private static bool IsReplayed(HttpResponseMessage response) =>
        response.Headers.TryGetValues(Idempotency.ReplayedHeaderName, out var values)
        && values.Contains("true");

    private Task<Coupon> StoredAsync(Guid couponId) =>
        DbAsync(db => db.Coupons.AsNoTracking().SingleAsync(c => c.Id == couponId));

    /// <summary>The cashier's redemption: a real purchase with the coupon's code.</summary>
    private async Task RedeemAsync(Customer customer, Guid couponId)
    {
        var email = $"activation-cashier-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        var cashier = await factory.CreateCashierAsync(email);
        var card = await customer.Client.IssueCardAsync();
        var code = (await StoredAsync(couponId)).Code;
        CashierTestHelpers.AssertCreated(
            await cashier.PurchaseAsync(CashierTestHelpers.Sale(card, code))
        );
        Assert.Equal(CouponStatus.Redeemed, (await StoredAsync(couponId)).Status);
    }

    [Fact]
    public async Task Activation_generates_a_code_that_both_owner_reads_return_and_moves_no_points()
    {
        var customer = await AccountAsync();
        var coupon = await CouponAsync(customer);
        Assert.Null(coupon.Code);
        Assert.Null(
            (await customer.Client.GetFromJsonAsync<CouponBody>($"{Coupons}/{coupon.Id}"))!.Code
        );
        var ledgerBefore = await DbAsync(db =>
            db.PointsLedgerEntries.CountAsync(e => e.UserId == customer.UserId)
        );

        var response = await ActivateAsync(customer.Client, coupon.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await BodyOf(response);
        Assert.Equal("Active", body.Status);
        Assert.True(CouponCode.IsValid(body.Code));
        Assert.NotNull(body.ActivatedAt);
        Assert.True(body.ActivatedAt >= body.PurchasedAt);
        Assert.Null(body.RedeemedAt);

        // The response carries the values that were stored.
        var stored = await StoredAsync(coupon.Id);
        Assert.Equal(CouponStatus.Active, stored.Status);
        Assert.Equal(body.Code, stored.Code);
        Assert.Equal(body.ActivatedAt, stored.ActivatedAt);

        Assert.Equal(
            body.Code,
            (await customer.Client.GetFromJsonAsync<CouponBody>($"{Coupons}/{coupon.Id}"))!.Code
        );
        Assert.Equal(
            body.Code,
            (await customer.Client.GetFromJsonAsync<ListBody<CouponBody>>(Coupons))!
                .Items.Single()
                .Code
        );

        Assert.Equal(
            500,
            await DbAsync(db =>
                db.LoyaltyAccounts.Where(a => a.UserId == customer.UserId)
                    .Select(a => a.Balance)
                    .SingleAsync()
            )
        );
        Assert.Equal(
            ledgerBefore,
            await DbAsync(db => db.PointsLedgerEntries.CountAsync(e => e.UserId == customer.UserId))
        );
    }

    [Fact]
    public async Task Activation_time_is_the_statement_time_not_the_transaction_start()
    {
        var customer = await AccountAsync();
        var coupon = await CouponAsync(customer);
        var key = Guid.NewGuid();

        var body = await BodyOf(await ActivateAsync(customer.Client, coupon.Id, key.ToString()));

        // The idempotency record is inserted with now() in the same transaction: with now() the
        // activation time would equal it exactly.
        var transactionStart = await DbAsync(db =>
            db.IdempotencyRecords.Where(r => r.UserId == customer.UserId && r.IdempotencyKey == key)
                .Select(r => r.CreatedAt)
                .SingleAsync()
        );
        Assert.True(
            body.ActivatedAt > transactionStart,
            $"{body.ActivatedAt:O} vs {transactionStart:O}"
        );
    }

    [Fact]
    public async Task Activating_again_with_a_new_key_returns_the_same_code()
    {
        var customer = await AccountAsync();
        var coupon = await CouponAsync(customer);

        var first = await BodyOf(await ActivateAsync(customer.Client, coupon.Id));
        var second = await ActivateAsync(customer.Client, coupon.Id);

        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        Assert.False(IsReplayed(second));
        var again = await BodyOf(second);
        Assert.Equal(first.Code, again.Code);
        Assert.Equal(first.ActivatedAt, again.ActivatedAt);
    }

    [Fact]
    public async Task A_retry_after_a_lost_response_replays_the_same_code()
    {
        var customer = await AccountAsync();
        var coupon = await CouponAsync(customer);
        var key = NewKey();

        var first = await BodyOf(await ActivateAsync(customer.Client, coupon.Id, key));
        var retry = await ActivateAsync(customer.Client, coupon.Id, key);

        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        Assert.True(IsReplayed(retry));
        Assert.Equal(first.Code, (await BodyOf(retry)).Code);
        Assert.Equal(first.Code, (await StoredAsync(coupon.Id)).Code);
    }

    [Fact]
    public async Task Parallel_activations_produce_exactly_one_code()
    {
        var customer = await AccountAsync();
        var coupon = await CouponAsync(customer);

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => ActivateAsync(customer.Client, coupon.Id))
        );

        Assert.All(responses, response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));
        var codes = await Task.WhenAll(
            responses.Select(async response => (await BodyOf(response)).Code)
        );
        var stored = (await StoredAsync(coupon.Id)).Code;
        Assert.All(codes, code => Assert.Equal(stored, code));
    }

    [Fact]
    public async Task Parallel_activations_with_one_key_produce_one_code()
    {
        var customer = await AccountAsync();
        var coupon = await CouponAsync(customer);
        var key = NewKey();

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 4).Select(_ => ActivateAsync(customer.Client, coupon.Id, key))
        );

        var stored = (await StoredAsync(coupon.Id)).Code;
        foreach (var response in responses)
        {
            // A request that waited too long for the first one gets in_progress; the rest see its result.
            if (response.StatusCode == HttpStatusCode.Conflict)
                Assert.Equal("idempotency.in_progress", await ErrorOf(response));
            else
                Assert.Equal(stored, (await BodyOf(response)).Code);
        }
        Assert.Contains(responses, response => response.StatusCode == HttpStatusCode.OK);
    }

    [Fact]
    public async Task A_redeemed_coupon_gives_409_and_never_becomes_active_again()
    {
        var customer = await AccountAsync();
        var coupon = await CouponAsync(customer);
        var code = (await BodyOf(await ActivateAsync(customer.Client, coupon.Id))).Code;
        await RedeemAsync(customer, coupon.Id);

        var response = await ActivateAsync(customer.Client, coupon.Id);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.Equal("rewards.coupon_redeemed", await ErrorOf(response));
        var stored = await StoredAsync(coupon.Id);
        Assert.Equal(CouponStatus.Redeemed, stored.Status);
        Assert.Equal(code, stored.Code);
    }

    [Fact]
    public async Task A_replay_after_redemption_returns_the_stored_response_but_get_shows_the_current_state()
    {
        var customer = await AccountAsync();
        var coupon = await CouponAsync(customer);
        var key = NewKey();
        await ActivateAsync(customer.Client, coupon.Id, key);
        await RedeemAsync(customer, coupon.Id);

        var replay = await ActivateAsync(customer.Client, coupon.Id, key);

        Assert.True(IsReplayed(replay));
        Assert.Equal("Active", (await BodyOf(replay)).Status);
        Assert.Equal(
            "Redeemed",
            (await customer.Client.GetFromJsonAsync<CouponBody>($"{Coupons}/{coupon.Id}"))!.Status
        );
    }

    [Fact]
    public async Task A_coupon_of_a_deactivated_offer_can_still_be_activated()
    {
        var customer = await AccountAsync();
        var coupon = await CouponAsync(customer, deactivateOffer: true);

        var response = await ActivateAsync(customer.Client, coupon.Id);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Active", (await BodyOf(response)).Status);
    }

    [Fact]
    public async Task Another_customers_coupon_or_a_missing_one_is_404_and_stays_untouched()
    {
        var owner = await AccountAsync();
        var other = await AccountAsync();
        var coupon = await CouponAsync(owner);

        var foreign = await ActivateAsync(other.Client, coupon.Id);
        var missing = await ActivateAsync(other.Client, Guid.NewGuid());

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("rewards.coupon_not_found", await ErrorOf(foreign));
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal("rewards.coupon_not_found", await ErrorOf(missing));
        var stored = await StoredAsync(coupon.Id);
        Assert.Equal(CouponStatus.Purchased, stored.Status);
        Assert.Null(stored.Code);

        // The owner's activation is not affected by the other customer's attempts.
        Assert.Equal(HttpStatusCode.OK, (await ActivateAsync(owner.Client, coupon.Id)).StatusCode);
        Assert.Equal(
            HttpStatusCode.NotFound,
            (await other.Client.GetAsync($"{Coupons}/{coupon.Id}")).StatusCode
        );
    }

    [Fact]
    public async Task Coupon_responses_are_never_stored_including_replays_and_errors()
    {
        await using var host = factory.WithoutPerRequestSessionRenewal();
        var customer = await AccountAsync(host: host);
        var coupon = await CouponAsync(customer);
        var key = NewKey();

        var responses = new[]
        {
            await ActivateAsync(customer.Client, coupon.Id, key),
            await ActivateAsync(customer.Client, coupon.Id, key),
            await ActivateAsync(customer.Client, Guid.NewGuid()),
            await customer.Client.GetAsync(Coupons),
            await customer.Client.GetAsync($"{Coupons}/{coupon.Id}"),
        };

        Assert.True(IsReplayed(responses[1]));
        Assert.Equal(HttpStatusCode.NotFound, responses[2].StatusCode);
        foreach (var response in responses)
            Assert.True(
                response.Headers.CacheControl?.NoStore,
                $"{response.RequestMessage!.RequestUri} {(int)response.StatusCode}"
            );
        // Control: a response without secrets has no such header, so the host does not add it everywhere.
        Assert.Null((await customer.Client.GetAsync("/api/me/wallet")).Headers.CacheControl);
    }

    [Fact]
    public async Task Coupon_codes_never_reach_the_logs()
    {
        var customer = await AccountAsync();
        var coupon = await CouponAsync(customer);
        var key = NewKey();
        var logsBefore = factory.Logs.Messages.Count;

        var code = (await BodyOf(await ActivateAsync(customer.Client, coupon.Id, key))).Code!;
        await ActivateAsync(customer.Client, coupon.Id, key);
        await ActivateAsync(customer.Client, coupon.Id);
        await customer.Client.GetAsync(Coupons);

        var messages = factory.Logs.Messages;
        Assert.True(messages.Count > logsBefore, "The capture must see the requests' logs.");
        Assert.Contains(messages, message => message.Contains("UPDATE \"Coupons\""));
        Assert.DoesNotContain(messages, message => message.Contains(code));
    }

    [Fact]
    public async Task Activation_requires_the_user_role_csrf_and_an_idempotency_key()
    {
        var customer = await AccountAsync();
        var coupon = await CouponAsync(customer);
        var cashier = await AccountAsync(RoleNames.Cashier, balance: null);
        var anonymous = factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsync($"{Coupons}/{coupon.Id}/activate", null)).StatusCode
        );
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (await ActivateAsync(cashier.Client, coupon.Id)).StatusCode
        );

        var noCsrf = await ActivateAsync(customer.Client, coupon.Id, csrf: false);
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        Assert.Equal("csrf.invalid", await ErrorOf(noCsrf));

        var noKey = await ActivateAsync(customer.Client, coupon.Id, withKey: false);
        Assert.Equal(HttpStatusCode.BadRequest, noKey.StatusCode);
        Assert.Equal("idempotency.key_required", await ErrorOf(noKey));

        Assert.Equal(CouponStatus.Purchased, (await StoredAsync(coupon.Id)).Status);
    }
}
