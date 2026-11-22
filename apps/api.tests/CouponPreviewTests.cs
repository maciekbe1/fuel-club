using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Rewards.Domain;
using FuelClub.Api.Modules.Rewards.Infrastructure;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuelClub.Api.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class CouponPreviewTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private const string Preview = "/api/cashier/coupons/preview";
    private const int TestPermitLimit = 10;
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

    private sealed record CalculationBody(decimal Discount, decimal PaidAmount, int PointsAwarded);

    private sealed record PreviewBody(
        string OfferName,
        decimal DiscountPerLiter,
        decimal MaxLiters,
        CalculationBody? Calculation
    );

    private sealed record ProblemBody(string? Code, Dictionary<string, FieldError[]>? Errors);

    private sealed record FieldError(string Field, string Code);

    private sealed record Customer(string UserId, string CardCode);

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private async Task<(HttpClient Client, string UserId)> AccountAsync(string role)
    {
        var email = $"preview-{role.ToLowerInvariant()}-{Guid.NewGuid():N}@example.test";
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

    private async Task<HttpClient> CashierAsync() => (await AccountAsync(RoleNames.Cashier)).Client;

    private async Task<Customer> CustomerAsync()
    {
        var (_, userId) = await AccountAsync(RoleNames.User);
        var card = new LoyaltyCard
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Code = LoyaltyCardCode.Generate(),
            IssuedAt = DateTimeOffset.UtcNow,
        };
        await DbAsync(async db =>
        {
            db.LoyaltyAccounts.Add(new LoyaltyAccount { UserId = userId, Balance = 700 });
            db.LoyaltyCards.Add(card);
            return await db.SaveChangesAsync();
        });
        return new Customer(userId, card.Code);
    }

    /// <summary>A coupon in the given state, inserted directly (the flows have their own tests).</summary>
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
            Code = status == CouponStatus.Purchased ? null : CouponCode.Generate(),
            ActivatedAt = status == CouponStatus.Purchased ? null : now.AddMinutes(-5),
        };
        FuelPurchase? purchase = null;
        if (status == CouponStatus.Redeemed)
        {
            purchase = new FuelPurchase
            {
                Id = Guid.CreateVersion7(),
                UserId = customer.UserId,
                RegisteredByUserId = customer.UserId,
                StationCode = "ST-001",
                RegisterCode = "K1",
                SaleDate = new DateOnly(2026, 11, 21),
                ReceiptNumber = Guid.NewGuid().ToString("N").ToUpperInvariant(),
                GrossAmount = 100m,
                Discount = 8m,
                PaidAmount = 92m,
                Liters = 40m,
                PointsAwarded = 92,
            };
            coupon.RedeemedAt = now;
            coupon.RedeemedPurchaseId = purchase.Id;
        }
        await DbAsync(async db =>
        {
            db.RewardOffers.Add(offer);
            if (purchase is not null)
                db.FuelPurchases.Add(purchase);
            db.Coupons.Add(coupon);
            return await db.SaveChangesAsync();
        });
        return coupon;
    }

    private static async Task<HttpResponseMessage> PreviewAsync(
        HttpClient client,
        object body,
        bool csrf = true
    )
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, Preview)
        {
            Content = JsonContent.Create(body),
        };
        if (csrf)
            request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        return await client.SendAsync(request);
    }

    private static Task<HttpResponseMessage> PreviewAsync(
        HttpClient client,
        string? cardCode,
        string? couponCode,
        decimal? amount = null,
        decimal? liters = null
    ) =>
        PreviewAsync(
            client,
            new
            {
                cardCode,
                couponCode,
                amount,
                liters,
            }
        );

    private static async Task<PreviewBody> BodyOf(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<PreviewBody>())!;
    }

    private static async Task<ProblemBody> ProblemOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>())!;

    [Fact]
    public async Task Without_amount_and_liters_returns_the_terms_only()
    {
        var cashier = await CashierAsync();
        var customer = await CustomerAsync();
        var coupon = await CouponAsync(customer, rate: 0.25m, liters: 40m);

        var body = await BodyOf(await PreviewAsync(cashier, customer.CardCode, coupon.Code));

        Assert.Equal(coupon.OfferName, body.OfferName);
        Assert.Equal(0.25m, body.DiscountPerLiter);
        Assert.Equal(40m, body.MaxLiters);
        Assert.Null(body.Calculation);
    }

    [Theory]
    [InlineData("0.20", "50", "40.5", "250.00", "8.10", "241.90", 241)]
    [InlineData("0.20", "50", "60.123", "400.00", "10.00", "390.00", 390)]
    [InlineData("0.15", "50", "0.3", "2.00", "0.05", "1.95", 1)]
    [InlineData("10.00", "100", "5", "30.00", "30.00", "0.00", 0)]
    public async Task With_amount_and_liters_returns_the_same_calculation_as_the_discount_table(
        string rate,
        string maxLiters,
        string liters,
        string gross,
        string discount,
        string paid,
        int points
    )
    {
        var cashier = await CashierAsync();
        var customer = await CustomerAsync();
        var coupon = await CouponAsync(customer, rate: Dec(rate), liters: Dec(maxLiters));

        var body = await BodyOf(
            await PreviewAsync(cashier, customer.CardCode, coupon.Code, Dec(gross), Dec(liters))
        );

        Assert.Equal(new CalculationBody(Dec(discount), Dec(paid), points), body.Calculation);
    }

    private static decimal Dec(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Fact]
    public async Task Codes_typed_by_hand_are_normalized()
    {
        var cashier = await CashierAsync();
        var customer = await CustomerAsync();
        var coupon = await CouponAsync(customer);
        var typedCoupon = string.Join('-', coupon.Code!.Chunk(4).Select(chunk => new string(chunk)))
            .ToLowerInvariant();

        await BodyOf(
            await PreviewAsync(
                cashier,
                " " + customer.CardCode.ToLowerInvariant() + " ",
                typedCoupon
            )
        );
    }

    [Theory]
    [InlineData("10.00", null, "liters", "rewards.required_field")]
    [InlineData(null, "5", "amount", "rewards.required_field")]
    [InlineData("0", "5", "amount", "loyalty.amount_out_of_range")]
    [InlineData("10.005", "5", "amount", "loyalty.amount_precision")]
    [InlineData("10.00", "0", "liters", "loyalty.liters_out_of_range")]
    [InlineData("10.00", "1.0005", "liters", "loyalty.liters_precision")]
    public async Task Amount_and_liters_come_together_and_follow_the_purchase_rules(
        string? amount,
        string? liters,
        string field,
        string code
    )
    {
        var cashier = await CashierAsync();
        var customer = await CustomerAsync();
        var coupon = await CouponAsync(customer);

        var response = await PreviewAsync(
            cashier,
            customer.CardCode,
            coupon.Code,
            amount is null ? null : Dec(amount),
            liters is null ? null : Dec(liters)
        );

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problem = await ProblemOf(response);
        Assert.Equal("rewards.validation_failed", problem.Code);
        Assert.Contains(problem.Errors![field], error => error.Code == code);
    }

    [Fact]
    public async Task Codes_are_required_and_a_card_code_is_not_a_coupon_code()
    {
        var cashier = await CashierAsync();
        var customer = await CustomerAsync();
        var coupon = await CouponAsync(customer);

        var missing = await ProblemOf(await PreviewAsync(cashier, null, " "));
        Assert.Contains(
            missing.Errors!["cardCode"],
            error => error.Code == "loyalty.required_field"
        );
        Assert.Contains(
            missing.Errors!["couponCode"],
            error => error.Code == "rewards.required_field"
        );

        var swapped = await ProblemOf(await PreviewAsync(cashier, coupon.Code, customer.CardCode));
        Assert.Contains(
            swapped.Errors!["cardCode"],
            error => error.Code == "loyalty.invalid_format"
        );
        Assert.Contains(
            swapped.Errors!["couponCode"],
            error => error.Code == "rewards.invalid_format"
        );
    }

    [Fact]
    public async Task Unknown_or_revoked_card_is_404()
    {
        var cashier = await CashierAsync();
        var customer = await CustomerAsync();
        var coupon = await CouponAsync(customer);

        var unknown = await PreviewAsync(cashier, LoyaltyCardCode.Generate(), coupon.Code);
        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal("loyalty.card_not_found", (await ProblemOf(unknown)).Code);

        await DbAsync(db =>
            db.LoyaltyCards.Where(c => c.UserId == customer.UserId)
                .ExecuteUpdateAsync(set =>
                    set.SetProperty(c => c.RevokedAt, DateTimeOffset.UtcNow.AddMinutes(1))
                )
        );
        var revoked = await PreviewAsync(cashier, customer.CardCode, coupon.Code);
        Assert.Equal(HttpStatusCode.NotFound, revoked.StatusCode);
        Assert.Equal("loyalty.card_not_found", (await ProblemOf(revoked)).Code);
    }

    [Fact]
    public async Task Someone_elses_coupon_looks_exactly_like_a_missing_one()
    {
        var cashier = await CashierAsync();
        var customer = await CustomerAsync();
        var other = await CustomerAsync();
        var foreignCoupon = await CouponAsync(other);

        var foreign = await PreviewAsync(cashier, customer.CardCode, foreignCoupon.Code);
        var missing = await PreviewAsync(cashier, customer.CardCode, CouponCode.Generate());

        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Equal(
            await missing.Content.ReadAsStringAsync(),
            await foreign.Content.ReadAsStringAsync()
        );
        Assert.Equal("rewards.coupon_not_found", (await ProblemOf(foreign)).Code);
    }

    [Fact]
    public async Task A_redeemed_coupon_is_409_and_a_redeemed_coupon_of_someone_else_is_404()
    {
        var cashier = await CashierAsync();
        var customer = await CustomerAsync();
        var other = await CustomerAsync();
        var redeemed = await CouponAsync(customer, CouponStatus.Redeemed);
        var foreignRedeemed = await CouponAsync(other, CouponStatus.Redeemed);

        var own = await PreviewAsync(cashier, customer.CardCode, redeemed.Code);
        Assert.Equal(HttpStatusCode.Conflict, own.StatusCode);
        Assert.Equal("rewards.coupon_redeemed", (await ProblemOf(own)).Code);

        var foreign = await PreviewAsync(cashier, customer.CardCode, foreignRedeemed.Code);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
    }

    [Fact]
    public async Task Preview_changes_and_reserves_nothing()
    {
        var cashier = await CashierAsync();
        var customer = await CustomerAsync();
        var coupon = await CouponAsync(customer);
        var before = await DbAsync(db =>
            db.Coupons.AsNoTracking().SingleAsync(c => c.Id == coupon.Id)
        );

        await BodyOf(await PreviewAsync(cashier, customer.CardCode, coupon.Code, 250m, 40.5m));
        await BodyOf(await PreviewAsync(cashier, customer.CardCode, coupon.Code, 250m, 40.5m));

        var after = await DbAsync(db =>
            db.Coupons.AsNoTracking().SingleAsync(c => c.Id == coupon.Id)
        );
        Assert.Equivalent(before, after);
        Assert.Equal(
            700,
            await DbAsync(db =>
                db.LoyaltyAccounts.Where(a => a.UserId == customer.UserId)
                    .Select(a => a.Balance)
                    .SingleAsync()
            )
        );
        Assert.Equal(
            0,
            await DbAsync(db => db.FuelPurchases.CountAsync(p => p.UserId == customer.UserId))
        );
        Assert.Equal(
            0,
            await DbAsync(db => db.PointsLedgerEntries.CountAsync(e => e.UserId == customer.UserId))
        );
    }

    [Fact]
    public async Task Each_cashier_has_a_limit_of_attempts_counting_failures_too()
    {
        var cashier = await CashierAsync();
        var otherCashier = await CashierAsync();
        var customer = await CustomerAsync();
        var coupon = await CouponAsync(customer);

        for (var attempt = 0; attempt < TestPermitLimit - 1; attempt++)
            Assert.Equal(
                HttpStatusCode.NotFound,
                (await PreviewAsync(cashier, LoyaltyCardCode.Generate(), coupon.Code)).StatusCode
            );
        await BodyOf(await PreviewAsync(cashier, customer.CardCode, coupon.Code));

        var limited = await PreviewAsync(cashier, customer.CardCode, coupon.Code);
        Assert.Equal(HttpStatusCode.TooManyRequests, limited.StatusCode);
        Assert.Equal(RateLimiting.ExceededCode, (await ProblemOf(limited)).Code);
        Assert.NotNull(limited.Headers.RetryAfter);

        await BodyOf(await PreviewAsync(otherCashier, customer.CardCode, coupon.Code));
    }

    [Fact]
    public async Task Codes_never_reach_the_logs()
    {
        var cashier = await CashierAsync();
        var customer = await CustomerAsync();
        var coupon = await CouponAsync(customer);
        var logsBefore = factory.Logs.Messages.Count;

        await BodyOf(await PreviewAsync(cashier, customer.CardCode, coupon.Code, 250m, 40.5m));
        await PreviewAsync(cashier, customer.CardCode, CouponCode.Generate());

        var messages = factory.Logs.Messages;
        Assert.True(messages.Count > logsBefore, "The capture must see the requests' logs.");
        Assert.Contains(messages, message => message.Contains("\"Coupons\""));
        Assert.DoesNotContain(
            messages,
            message => message.Contains(customer.CardCode) || message.Contains(coupon.Code!)
        );
    }

    [Theory]
    [InlineData(RoleNames.User)]
    [InlineData(RoleNames.Admin)]
    public async Task Only_cashiers_may_preview(string role)
    {
        var (client, _) = await AccountAsync(role);
        Assert.Equal(
            HttpStatusCode.Forbidden,
            (
                await PreviewAsync(client, LoyaltyCardCode.Generate(), CouponCode.Generate())
            ).StatusCode
        );
    }

    [Fact]
    public async Task Preview_requires_login_and_csrf()
    {
        var cashier = await CashierAsync();
        var anonymous = factory.CreateClient();

        Assert.Equal(
            HttpStatusCode.Unauthorized,
            (await anonymous.PostAsJsonAsync(Preview, new { })).StatusCode
        );
        var noCsrf = await PreviewAsync(
            cashier,
            new { cardCode = LoyaltyCardCode.Generate(), couponCode = CouponCode.Generate() },
            csrf: false
        );
        Assert.Equal(HttpStatusCode.Forbidden, noCsrf.StatusCode);
        Assert.Equal("csrf.invalid", (await ProblemOf(noCsrf)).Code);
    }
}

public sealed class PreviewRateLimitOptionsTests
{
    [Theory]
    [InlineData(0, "00:01:00", false)]
    [InlineData(1001, "00:01:00", false)]
    [InlineData(30, "00:00:00.5", false)]
    [InlineData(30, "01:00:01", false)]
    [InlineData(1, "00:00:01", true)]
    [InlineData(1000, "01:00:00", true)]
    public void Preview_limit_and_window_are_bounded(int permits, string window, bool valid) =>
        Assert.Equal(
            valid,
            new RewardsOptionsValidator()
                .Validate(
                    null,
                    new RewardsOptions
                    {
                        PreviewPermitLimit = permits,
                        PreviewWindow = TimeSpan.Parse(window, CultureInfo.InvariantCulture),
                    }
                )
                .Succeeded
        );
}
