using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Cashier;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using FuelClub.Api.Modules.Rewards.Domain;
using FuelClub.Api.Modules.Rewards.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FuelClub.Api.Tests;

/// <summary>Redemption of a coupon together with the cashier's fuel purchase.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class CouponRedemptionTests(ApiFactory factory) : IAsyncLifetime
{
    private const int StartBalance = 700;
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

    private sealed record PurchaseBody(
        Guid PurchaseId,
        int PointsAwarded,
        decimal Discount,
        decimal PaidAmount
    );

    private sealed record ProblemBody(string? Code, Dictionary<string, FieldError[]>? Errors);

    private sealed record FieldError(string Field, string Code);

    private sealed record HistoryPurchase(
        Guid PurchaseId,
        decimal Amount,
        decimal GrossAmount,
        decimal Discount,
        decimal PaidAmount
    );

    private sealed record HistoryItem(int Delta, HistoryPurchase? FuelPurchase);

    private sealed record HistoryBody(List<HistoryItem> Items);

    private sealed record CouponBody(Guid Id, string Status, DateTimeOffset? RedeemedAt);

    private sealed record Customer(HttpClient Client, string UserId, string CardCode);

    /// <summary>What a failed or rejected purchase must leave untouched.</summary>
    private sealed record State(
        int Balance,
        int Ledger,
        int Purchases,
        CouponStatus Coupon,
        int CashierRecords
    );

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private string NewEmail(string kind)
    {
        var email = $"redeem-{kind}-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        return email;
    }

    private async Task<Customer> CustomerAsync()
    {
        var email = NewEmail("customer");
        await factory.CreateTestUserAsync(email, CashierTestHelpers.Password, RoleNames.User);
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, CashierTestHelpers.Password);
        var userId = await DbAsync(db =>
            db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync()
        );
        var card = await client.IssueCardAsync();
        await DbAsync(db =>
            db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "LoyaltyAccounts" ("UserId", "Balance") VALUES ({userId}, {StartBalance})
                ON CONFLICT ("UserId") DO UPDATE SET "Balance" = EXCLUDED."Balance"
                """
            )
        );
        return new Customer(client, userId, card);
    }

    private async Task<(HttpClient Client, string UserId)> CashierAsync()
    {
        var email = NewEmail("cashier");
        var client = await factory.CreateCashierAsync(email);
        var userId = await DbAsync(db =>
            db.Users.Where(u => u.Email == email).Select(u => u.Id).SingleAsync()
        );
        return (client, userId);
    }

    /// <summary>A coupon inserted directly (exchange and activation have their own tests).</summary>
    private async Task<Coupon> CouponAsync(
        Customer customer,
        CouponStatus status = CouponStatus.Active,
        decimal rate = 0.20m,
        decimal liters = 50m
    )
    {
        var offer = new RewardOffer
        {
            Id = Guid.CreateVersion7(),
            Name = $"Rabat {_tag}",
            Description = "Na dowolne paliwo",
            PointsCost = 500,
            DiscountPerLiter = rate,
            MaxLiters = liters,
            IsActive = true,
        };
        var now = DateTimeOffset.UtcNow;
        var active = status == CouponStatus.Active;
        var coupon = new Coupon
        {
            Id = Guid.CreateVersion7(),
            UserId = customer.UserId,
            OfferId = offer.Id,
            OfferName = offer.Name,
            PointsCost = offer.PointsCost,
            DiscountPerLiter = rate,
            MaxLiters = liters,
            Status = status,
            PurchasedAt = now.AddMinutes(-10),
            Code = active ? CouponCode.Generate() : null,
            ActivatedAt = active ? now.AddMinutes(-5) : null,
        };
        await DbAsync(async db =>
        {
            db.RewardOffers.Add(offer);
            db.Coupons.Add(coupon);
            return await db.SaveChangesAsync();
        });
        return coupon;
    }

    private Task<Coupon> StoredAsync(Guid couponId) =>
        DbAsync(db => db.Coupons.AsNoTracking().SingleAsync(c => c.Id == couponId));

    private async Task<State> StateAsync(Customer customer, Guid couponId, string cashierId) =>
        new(
            await DbAsync(db =>
                db.LoyaltyAccounts.Where(a => a.UserId == customer.UserId)
                    .Select(a => a.Balance)
                    .SingleAsync()
            ),
            await DbAsync(db =>
                db.PointsLedgerEntries.CountAsync(e => e.UserId == customer.UserId)
            ),
            await DbAsync(db => db.FuelPurchases.CountAsync(p => p.UserId == customer.UserId)),
            (await StoredAsync(couponId)).Status,
            await DbAsync(db => db.IdempotencyRecords.CountAsync(r => r.UserId == cashierId))
        );

    private static async Task<PurchaseBody> CreatedBody(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PurchaseBody>())!;
    }

    private static async Task<ProblemBody> ProblemOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>())!;

    private static bool IsReplayed(HttpResponseMessage response) =>
        response.Headers.TryGetValues(
            FuelClub.Api.Infrastructure.Idempotency.Idempotency.ReplayedHeaderName,
            out var values
        ) && values.Contains("true");

    [Fact]
    public async Task Redemption_discounts_the_purchase_awards_points_on_the_paid_amount_and_redeems_the_coupon()
    {
        var customer = await CustomerAsync();
        var (cashier, cashierId) = await CashierAsync();
        var coupon = await CouponAsync(customer);
        var key = Guid.NewGuid().ToString();

        var body = await CreatedBody(
            await cashier.PurchaseAsync(
                CashierTestHelpers.Sale(customer.CardCode, coupon.Code, 100m, 40m),
                key
            )
        );

        Assert.Equal((92, 8m, 92m), (body.PointsAwarded, body.Discount, body.PaidAmount));
        var purchase = await DbAsync(db =>
            db.FuelPurchases.AsNoTracking().SingleAsync(p => p.Id == body.PurchaseId)
        );
        Assert.Equal(
            (customer.UserId, 100m, 8m, 92m, 92),
            (
                purchase.UserId,
                purchase.GrossAmount,
                purchase.Discount,
                purchase.PaidAmount,
                purchase.PointsAwarded
            )
        );
        Assert.Equal(StartBalance + 92, (await StateAsync(customer, coupon.Id, cashierId)).Balance);

        var stored = await StoredAsync(coupon.Id);
        Assert.Equal(CouponStatus.Redeemed, stored.Status);
        Assert.Equal(body.PurchaseId, stored.RedeemedPurchaseId);
        Assert.Equal(coupon.Code, stored.Code);
        Assert.True(stored.RedeemedAt > stored.ActivatedAt);
        // The idempotency record is written with now() (transaction start); redemption uses the statement time.
        var transactionStart = await DbAsync(db =>
            db.IdempotencyRecords.Where(r =>
                    r.UserId == cashierId && r.IdempotencyKey == Guid.Parse(key)
                )
                .Select(r => r.CreatedAt)
                .SingleAsync()
        );
        Assert.True(stored.RedeemedAt > transactionStart);

        var history = await customer.Client.GetFromJsonAsync<HistoryBody>("/api/me/transactions");
        var entry = Assert.Single(history!.Items, item => item.FuelPurchase is not null);
        Assert.Equal(92, entry.Delta);
        Assert.Equal(new HistoryPurchase(body.PurchaseId, 100m, 100m, 8m, 92m), entry.FuelPurchase);

        var mine = await customer.Client.GetFromJsonAsync<CouponBody>(
            $"/api/me/coupons/{coupon.Id}"
        );
        Assert.Equal(("Redeemed", stored.RedeemedAt), (mine!.Status, mine.RedeemedAt));
    }

    [Fact]
    public async Task A_discount_covering_the_whole_amount_redeems_the_coupon_without_points_or_history()
    {
        var customer = await CustomerAsync();
        var (cashier, cashierId) = await CashierAsync();
        var coupon = await CouponAsync(customer);

        var body = await CreatedBody(
            await cashier.PurchaseAsync(
                CashierTestHelpers.Sale(customer.CardCode, coupon.Code, 5m, 40m)
            )
        );

        Assert.Equal((0, 5m, 0m), (body.PointsAwarded, body.Discount, body.PaidAmount));
        var state = await StateAsync(customer, coupon.Id, cashierId);
        Assert.Equal(
            (StartBalance, 0, 1, CouponStatus.Redeemed),
            (state.Balance, state.Ledger, state.Purchases, state.Coupon)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task An_omitted_or_null_coupon_code_is_a_plain_purchase(bool sendNull)
    {
        var customer = await CustomerAsync();
        var (cashier, cashierId) = await CashierAsync();
        var coupon = await CouponAsync(customer);
        object sale = sendNull
            ? CashierTestHelpers.Sale(customer.CardCode, null, 50m, 20m)
            : new
            {
                cardCode = customer.CardCode,
                amount = 50m,
                liters = 20m,
                saleDate = DateOnly.FromDateTime(DateTime.UtcNow).ToString("yyyy-MM-dd"),
                receiptNumber = CashierTestHelpers.NewReceipt(),
            };

        var body = await CreatedBody(await cashier.PurchaseAsync(sale));

        Assert.Equal((50, 0m, 50m), (body.PointsAwarded, body.Discount, body.PaidAmount));
        Assert.Equal(
            CouponStatus.Active,
            (await StateAsync(customer, coupon.Id, cashierId)).Coupon
        );
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("K123")]
    [InlineData("not a coupon code at all")]
    public async Task A_blank_or_malformed_coupon_code_is_a_field_error_that_is_not_stored(
        string couponCode
    )
    {
        var customer = await CustomerAsync();
        var (cashier, cashierId) = await CashierAsync();
        var coupon = await CouponAsync(customer);
        var before = await StateAsync(customer, coupon.Id, cashierId);

        var response = await cashier.PurchaseAsync(
            CashierTestHelpers.Sale(customer.CardCode, couponCode)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ProblemOf(response);
        Assert.Equal("loyalty.validation_failed", problem.Code);
        Assert.Equal("rewards.invalid_format", Assert.Single(problem.Errors!["couponCode"]).Code);
        Assert.Equal(before, await StateAsync(customer, coupon.Id, cashierId));
    }

    [Fact]
    public async Task A_coupon_code_typed_by_hand_is_normalized()
    {
        var customer = await CustomerAsync();
        var (cashier, _) = await CashierAsync();
        var coupon = await CouponAsync(customer);
        var typed = string.Join('-', coupon.Code!.Chunk(4).Select(chunk => new string(chunk)))
            .ToLowerInvariant();

        var body = await CreatedBody(
            await cashier.PurchaseAsync(
                CashierTestHelpers.Sale(customer.CardCode, " " + typed + " ")
            )
        );

        Assert.Equal(8m, body.Discount);
    }

    [Fact]
    public async Task Someone_elses_coupon_looks_exactly_like_an_unknown_one_and_changes_nothing()
    {
        var customer = await CustomerAsync();
        var other = await CustomerAsync();
        var (cashier, cashierId) = await CashierAsync();
        var foreignCoupon = await CouponAsync(other);
        var before = await StateAsync(other, foreignCoupon.Id, cashierId);

        var foreign = await cashier.PurchaseAsync(
            CashierTestHelpers.Sale(customer.CardCode, foreignCoupon.Code)
        );
        var unknown = await cashier.PurchaseAsync(
            CashierTestHelpers.Sale(customer.CardCode, CouponCode.Generate())
        );

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(
            await unknown.Content.ReadAsStringAsync(),
            await foreign.Content.ReadAsStringAsync()
        );
        Assert.Equal("rewards.coupon_not_found", (await ProblemOf(foreign)).Code);
        // Domain 4xx are stored by idempotency (2 records), everything else is unchanged.
        Assert.Equal(
            before with
            {
                CashierRecords = 2,
            },
            await StateAsync(other, foreignCoupon.Id, cashierId)
        );
        Assert.Equal(
            0,
            await DbAsync(db => db.FuelPurchases.CountAsync(p => p.UserId == customer.UserId))
        );
    }

    [Fact]
    public async Task A_coupon_that_is_not_activated_cannot_be_found_by_any_code()
    {
        var customer = await CustomerAsync();
        var (cashier, _) = await CashierAsync();
        var coupon = await CouponAsync(customer, CouponStatus.Purchased);

        var response = await cashier.PurchaseAsync(
            CashierTestHelpers.Sale(customer.CardCode, CouponCode.Generate())
        );

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(CouponStatus.Purchased, (await StoredAsync(coupon.Id)).Status);
    }

    [Fact]
    public async Task A_redeemed_coupon_conflicts_and_changes_nothing()
    {
        var customer = await CustomerAsync();
        var (cashier, cashierId) = await CashierAsync();
        var coupon = await CouponAsync(customer);
        var first = await CreatedBody(
            await cashier.PurchaseAsync(CashierTestHelpers.Sale(customer.CardCode, coupon.Code))
        );
        var before = await StateAsync(customer, coupon.Id, cashierId);

        var second = await cashier.PurchaseAsync(
            CashierTestHelpers.Sale(customer.CardCode, coupon.Code)
        );

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        Assert.Equal("rewards.coupon_redeemed", (await ProblemOf(second)).Code);
        Assert.Equal(
            before with
            {
                CashierRecords = before.CashierRecords + 1,
            },
            await StateAsync(customer, coupon.Id, cashierId)
        );
        Assert.Equal(first.PurchaseId, (await StoredAsync(coupon.Id)).RedeemedPurchaseId);
    }

    [Fact]
    public async Task A_duplicate_receipt_is_reported_before_a_redeemed_coupon()
    {
        var customer = await CustomerAsync();
        var (cashier, _) = await CashierAsync();
        var coupon = await CouponAsync(customer);
        var receipt = CashierTestHelpers.NewReceipt();
        await CreatedBody(
            await cashier.PurchaseAsync(
                CashierTestHelpers.Sale(customer.CardCode, coupon.Code, receipt: receipt)
            )
        );

        // A retry after a lost response, but with a new key.
        var retry = await cashier.PurchaseAsync(
            CashierTestHelpers.Sale(customer.CardCode, coupon.Code, receipt: receipt)
        );

        Assert.Equal(HttpStatusCode.Conflict, retry.StatusCode);
        Assert.Equal("loyalty.receipt_already_registered", (await ProblemOf(retry)).Code);
    }

    [Fact]
    public async Task A_duplicate_receipt_leaves_an_active_coupon_untouched()
    {
        var customer = await CustomerAsync();
        var (cashier, cashierId) = await CashierAsync();
        var coupon = await CouponAsync(customer);
        var receipt = CashierTestHelpers.NewReceipt();
        await CreatedBody(
            await cashier.PurchaseAsync(
                CashierTestHelpers.Sale(customer.CardCode, null, receipt: receipt)
            )
        );
        var before = await StateAsync(customer, coupon.Id, cashierId);

        var duplicate = await cashier.PurchaseAsync(
            CashierTestHelpers.Sale(customer.CardCode, coupon.Code, receipt: receipt)
        );

        Assert.Equal("loyalty.receipt_already_registered", (await ProblemOf(duplicate)).Code);
        Assert.Equal(
            before with
            {
                CashierRecords = before.CashierRecords + 1,
            },
            await StateAsync(customer, coupon.Id, cashierId)
        );
    }

    [Fact]
    public async Task Concurrent_purchases_with_one_coupon_and_different_keys_redeem_it_once()
    {
        var customer = await CustomerAsync();
        var (cashier, cashierId) = await CashierAsync();
        var coupon = await CouponAsync(customer);

        var responses = await Task.WhenAll(
            Enumerable
                .Range(0, 2)
                .Select(_ =>
                    cashier.PurchaseAsync(CashierTestHelpers.Sale(customer.CardCode, coupon.Code))
                )
        );

        Assert.Equal(
            [HttpStatusCode.Created, HttpStatusCode.Conflict],
            responses.Select(r => r.StatusCode).Order()
        );
        var conflict = responses.Single(r => r.StatusCode == HttpStatusCode.Conflict);
        Assert.Equal("rewards.coupon_redeemed", (await ProblemOf(conflict)).Code);
        var state = await StateAsync(customer, coupon.Id, cashierId);
        Assert.Equal((StartBalance + 92, 1, 1), (state.Balance, state.Ledger, state.Purchases));
    }

    [Fact]
    public async Task A_retry_with_the_same_key_replays_the_redemption_once()
    {
        var customer = await CustomerAsync();
        var (cashier, cashierId) = await CashierAsync();
        var coupon = await CouponAsync(customer);
        var sale = CashierTestHelpers.Sale(customer.CardCode, coupon.Code);
        var key = Guid.NewGuid().ToString();

        var first = await cashier.PurchaseAsync(sale, key);
        var replay = await cashier.PurchaseAsync(sale, key);

        Assert.True(IsReplayed(replay));
        Assert.Equal(
            await first.Content.ReadAsStringAsync(),
            await replay.Content.ReadAsStringAsync()
        );
        var state = await StateAsync(customer, coupon.Id, cashierId);
        Assert.Equal((StartBalance + 92, 1), (state.Balance, state.Purchases));
    }

    [Fact]
    public async Task An_activation_in_progress_does_not_expose_the_code_yet()
    {
        var customer = await CustomerAsync();
        var (cashier, _) = await CashierAsync();
        var coupon = await CouponAsync(customer, CouponStatus.Purchased);
        var code = CouponCode.Generate();

        await using (var activation = await RawTransactionAsync())
        {
            await activation.ExecuteAsync(ActivateSql(coupon.Id, code));

            var response = await cashier.PurchaseAsync(
                CashierTestHelpers.Sale(customer.CardCode, code)
            );

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }
        Assert.Equal(CouponStatus.Purchased, (await StoredAsync(coupon.Id)).Status);
    }

    [Fact]
    public async Task An_activation_committed_while_the_purchase_waits_for_the_customer_lock_is_seen()
    {
        var customer = await CustomerAsync();
        var (cashier, _) = await CashierAsync();
        var coupon = await CouponAsync(customer, CouponStatus.Purchased);
        var code = CouponCode.Generate();

        Task<HttpResponseMessage> purchase;
        await using (var customerLock = await RawTransactionAsync())
        {
            await customerLock.ExecuteAsync(
                "SELECT pg_advisory_xact_lock(hashtextextended(@p0, 0))",
                CustomerLock.Key(customer.UserId)
            );
            purchase = cashier.PurchaseAsync(CashierTestHelpers.Sale(customer.CardCode, code));
            await WaitForAdvisoryWaiterAsync();

            await using (var activation = await RawTransactionAsync())
            {
                await activation.ExecuteAsync(ActivateSql(coupon.Id, code));
                await activation.CommitAsync();
            }
        }

        var body = await CreatedBody(await purchase);
        Assert.Equal(8m, body.Discount);
        Assert.Equal(CouponStatus.Redeemed, (await StoredAsync(coupon.Id)).Status);
    }

    [Fact]
    public async Task A_locked_coupon_row_gives_a_retryable_503_that_is_not_stored()
    {
        var customer = await CustomerAsync();
        var (cashier, cashierId) = await CashierAsync();
        var coupon = await CouponAsync(customer);
        var sale = CashierTestHelpers.Sale(customer.CardCode, coupon.Code);
        var key = Guid.NewGuid().ToString();
        var before = await StateAsync(customer, coupon.Id, cashierId);

        await using (var rowLock = await RawTransactionAsync())
        {
            await rowLock.ExecuteAsync(
                """SELECT 1 FROM "Coupons" WHERE "Id" = @p0 FOR UPDATE""",
                coupon.Id
            );

            var busy = await cashier.PurchaseAsync(sale, key);

            Assert.Equal(HttpStatusCode.ServiceUnavailable, busy.StatusCode);
            Assert.Equal("rewards.coupon_busy", (await ProblemOf(busy)).Code);
            Assert.Equal("1", busy.Headers.GetValues("Retry-After").Single());
            Assert.Equal(before, await StateAsync(customer, coupon.Id, cashierId));
        }

        var retry = await cashier.PurchaseAsync(sale, key);
        Assert.False(IsReplayed(retry));
        Assert.Equal(8m, (await CreatedBody(retry)).Discount);
    }

    [Theory]
    [InlineData("FuelPurchases")]
    [InlineData("PointsLedgerEntries")]
    public async Task A_failure_while_saving_the_purchase_leaves_the_coupon_active_and_points_unchanged(
        string failingTable
    )
    {
        var customer = await CustomerAsync();
        var (cashier, cashierId) = await CashierAsync();
        var coupon = await CouponAsync(customer);
        var sale = CashierTestHelpers.Sale(customer.CardCode, coupon.Code);
        var key = Guid.NewGuid().ToString();
        var before = await StateAsync(customer, coupon.Id, cashierId);
        var function = $"test_fail_{_tag}";

        // Fails only this customer's rows, so other test data is unaffected.
        await DbAsync(db =>
            db.Database.ExecuteSqlRawAsync(
                $"""
                CREATE FUNCTION {function}() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW."UserId" = '{customer.UserId}' THEN
                        RAISE EXCEPTION 'test failure';
                    END IF;
                    RETURN NEW;
                END $$;
                CREATE TRIGGER {function} BEFORE INSERT ON "{failingTable}"
                    FOR EACH ROW EXECUTE FUNCTION {function}();
                """
            )
        );
        try
        {
            var failed = await cashier.PurchaseAsync(sale, key);

            Assert.Equal(HttpStatusCode.InternalServerError, failed.StatusCode);
            Assert.Equal(before, await StateAsync(customer, coupon.Id, cashierId));
            var stored = await StoredAsync(coupon.Id);
            Assert.Equal(
                (CouponStatus.Active, null, null),
                (stored.Status, stored.RedeemedAt, stored.RedeemedPurchaseId)
            );
        }
        finally
        {
            await DbAsync(db =>
                db.Database.ExecuteSqlRawAsync(
                    $"""
                    DROP TRIGGER {function} ON "{failingTable}";
                    DROP FUNCTION {function}();
                    """
                )
            );
        }

        Assert.Equal(8m, (await CreatedBody(await cashier.PurchaseAsync(sale, key))).Discount);
    }

    [Fact]
    public async Task Coupon_codes_from_the_purchase_request_never_reach_the_logs()
    {
        var customer = await CustomerAsync();
        var (cashier, _) = await CashierAsync();
        var coupon = await CouponAsync(customer);
        var unknown = CouponCode.Generate();
        var before = factory.Logs.Messages.Count;

        await cashier.PurchaseAsync(CashierTestHelpers.Sale(customer.CardCode, coupon.Code));
        await cashier.PurchaseAsync(CashierTestHelpers.Sale(customer.CardCode, unknown));

        var messages = factory.Logs.Messages;
        Assert.True(messages.Count > before);
        Assert.Contains(messages, m => m.Contains("Coupons"));
        Assert.DoesNotContain(messages, m => m.Contains(coupon.Code!) || m.Contains(unknown));
    }

    [Fact]
    public async Task Redemption_is_refused_without_a_hold_in_the_current_transaction()
    {
        var customer = await CustomerAsync();
        var coupon = await CouponAsync(customer);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var redemption = scope.ServiceProvider.GetRequiredService<ICouponRedemption>();
        var http = new Microsoft.AspNetCore.Http.DefaultHttpContext();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            redemption.MarkRedeemedAsync(coupon.Id, customer.UserId, Guid.NewGuid(), default)
        );

        await using (await db.Database.BeginTransactionAsync())
        {
            var held = await redemption.HoldAsync(
                http,
                customer.UserId,
                coupon.Code!,
                40m,
                100m,
                default
            );
            Assert.NotNull(held.Hold);
            // The owner is part of the update condition, not only of the hold.
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                redemption.MarkRedeemedAsync(coupon.Id, "someone-else", Guid.NewGuid(), default)
            );
        }
        await using (await db.Database.BeginTransactionAsync())
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                redemption.MarkRedeemedAsync(coupon.Id, customer.UserId, Guid.NewGuid(), default)
            );
        Assert.Equal(CouponStatus.Active, (await StoredAsync(coupon.Id)).Status);
    }

    [Theory]
    [InlineData("00:00:00", false)]
    [InlineData("00:00:30.001", false)]
    [InlineData("00:00:00.001", true)]
    [InlineData("00:00:30", true)]
    public void Coupon_lock_timeout_must_be_between_1_ms_and_30_s(string value, bool valid) =>
        Assert.Equal(
            valid,
            new RewardsOptionsValidator()
                .Validate(
                    null,
                    new RewardsOptions
                    {
                        CouponLockTimeout = TimeSpan.Parse(value, CultureInfo.InvariantCulture),
                    }
                )
                .Succeeded
        );

    private static FormattableSql ActivateSql(Guid couponId, string code) =>
        new(
            """
            UPDATE "Coupons" SET "Status" = 'Active', "Code" = @p0, "ActivatedAt" = clock_timestamp()
            WHERE "Id" = @p1 AND "Status" = 'Purchased'
            """,
            [code, couponId]
        );

    private sealed record FormattableSql(string Sql, object[] Parameters);

    private async Task WaitForAdvisoryWaiterAsync()
    {
        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (DateTime.UtcNow < deadline)
        {
            var waiting = await DbAsync(db =>
                db.Database.SqlQuery<int>(
                        $"""SELECT count(*)::int AS "Value" FROM pg_locks WHERE locktype = 'advisory' AND NOT granted"""
                    )
                    .SingleAsync()
            );
            if (waiting > 0)
                return;
            await Task.Delay(10);
        }
        throw new TimeoutException("Expected the purchase to wait for the customer lock.");
    }

    private async Task<RawTransaction> RawTransactionAsync()
    {
        var connection = new NpgsqlConnection(
            factory
                .Services.GetRequiredService<IConfiguration>()
                .GetConnectionString("DefaultConnection")
        );
        await connection.OpenAsync();
        return new RawTransaction(connection, await connection.BeginTransactionAsync());
    }

    /// <summary>A second session that holds its locks until disposed (rolled back) or committed.</summary>
    private sealed class RawTransaction(NpgsqlConnection connection, NpgsqlTransaction transaction)
        : IAsyncDisposable
    {
        private bool _committed;

        public Task ExecuteAsync(FormattableSql sql) => ExecuteAsync(sql.Sql, sql.Parameters);

        public async Task ExecuteAsync(string sql, params object[] parameters)
        {
            await using var command = new NpgsqlCommand(sql, connection, transaction);
            for (var i = 0; i < parameters.Length; i++)
                command.Parameters.AddWithValue($"p{i}", parameters[i]);
            await command.ExecuteNonQueryAsync();
        }

        public async Task CommitAsync()
        {
            await transaction.CommitAsync();
            _committed = true;
        }

        public async ValueTask DisposeAsync()
        {
            if (!_committed)
                await transaction.RollbackAsync();
            await connection.DisposeAsync();
        }
    }
}
