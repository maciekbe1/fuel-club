using System.Net;
using System.Net.Http.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Infrastructure.Idempotency;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FuelClub.Api.Tests;

/// <summary>Product rewards (ADR 0013): admin, exchange, activation and the handover at the till.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProductRewardsTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Handovers = "/api/cashier/reward-handovers";
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

    private sealed record ProblemBody(string? Code, Dictionary<string, FieldError[]>? Errors);

    private sealed record FieldError(string Field, string Code);

    private sealed record OfferBody(
        Guid Id,
        string Kind,
        decimal? DiscountPerLiter,
        decimal? MaxLiters,
        int Version
    );

    private sealed record CouponBody(
        Guid Id,
        string Kind,
        string OfferName,
        int PointsCost,
        string Status,
        string? Code
    );

    private sealed record ExchangeBody(CouponBody Coupon, int Balance);

    private sealed record HandoverBody(Guid CouponId, string OfferName);

    private sealed record PreviewBody(string Kind, object? Calculation);

    private sealed record Customer(HttpClient Client, string UserId, string CardCode);

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private string NewEmail(string kind)
    {
        var email = $"product-{kind}-{Guid.NewGuid():N}@example.test";
        _emails.Add(email);
        return email;
    }

    private async Task<HttpClient> AccountAsync(string role)
    {
        var email = NewEmail(role.ToLowerInvariant());
        await factory.CreateTestUserAsync(email, CashierTestHelpers.Password, role);
        var client = factory.CreateClient(
            new WebApplicationFactoryClientOptions { HandleCookies = true }
        );
        await client.LoginAsync(email, CashierTestHelpers.Password);
        return client;
    }

    private async Task<Customer> CustomerAsync(int balance = 1000)
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
                INSERT INTO "LoyaltyAccounts" ("UserId", "Balance") VALUES ({userId}, {balance})
                ON CONFLICT ("UserId") DO UPDATE SET "Balance" = EXCLUDED."Balance"
                """
            )
        );
        return new Customer(client, userId, card);
    }

    private static async Task<HttpResponseMessage> SendAsync(
        HttpClient client,
        HttpMethod method,
        string url,
        object? body,
        string? key = null
    )
    {
        using var request = new HttpRequestMessage(method, url)
        {
            Content = body is null ? null : JsonContent.Create(body),
        };
        request.Headers.Add(Idempotency.HeaderName, key ?? Guid.NewGuid().ToString());
        request.Headers.Add(TestHttpExtensions.CsrfHeader, await client.GetCsrfTokenAsync());
        return await client.SendAsync(request);
    }

    private static async Task<string?> CodeOf(HttpResponseMessage response) =>
        (await response.Content.ReadFromJsonAsync<ProblemBody>())!.Code;

    private async Task<RewardOffer> OfferAsync(RewardKind kind, string label = "Kawa")
    {
        var offer = new RewardOffer
        {
            Id = Guid.CreateVersion7(),
            Name = $"{label} {_tag}",
            Description = "",
            PointsCost = 150,
            Kind = kind,
            DiscountPerLiter = kind == RewardKind.FuelDiscount ? 0.20m : null,
            MaxLiters = kind == RewardKind.FuelDiscount ? 50m : null,
            IsActive = true,
        };
        await DbAsync(async db =>
        {
            db.RewardOffers.Add(offer);
            return await db.SaveChangesAsync();
        });
        return offer;
    }

    /// <summary>Exchanged and activated through the API: returns the coupon with its code.</summary>
    private async Task<CouponBody> ActiveCouponAsync(Customer customer, RewardOffer offer)
    {
        var exchange = await SendAsync(
            customer.Client,
            HttpMethod.Post,
            $"/api/rewards/{offer.Id}/exchange",
            new { version = offer.Version }
        );
        Assert.Equal(HttpStatusCode.Created, exchange.StatusCode);
        var coupon = (await exchange.Content.ReadFromJsonAsync<ExchangeBody>())!.Coupon;
        var activate = await SendAsync(
            customer.Client,
            HttpMethod.Post,
            $"/api/me/coupons/{coupon.Id}/activate",
            null
        );
        Assert.Equal(HttpStatusCode.OK, activate.StatusCode);
        return (await activate.Content.ReadFromJsonAsync<CouponBody>())!;
    }

    private static Task<HttpResponseMessage> HandOverAsync(
        HttpClient cashier,
        string cardCode,
        string couponCode,
        string? key = null
    ) => SendAsync(cashier, HttpMethod.Post, Handovers, new { cardCode, couponCode }, key);

    [Fact]
    public async Task Admin_creates_a_product_without_per_liter_terms_and_cannot_change_its_kind()
    {
        var admin = await AccountAsync(RoleNames.Admin);
        var name = $"Kawa {_tag}";

        var created = await SendAsync(
            admin,
            HttpMethod.Post,
            "/api/admin/rewards",
            new
            {
                name,
                description = "",
                pointsCost = 150,
                kind = "Product",
                isActive = true,
            }
        );
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var offer = (await created.Content.ReadFromJsonAsync<OfferBody>())!;
        Assert.Equal(
            ("Product", (decimal?)null, (decimal?)null),
            (offer.Kind, offer.DiscountPerLiter, offer.MaxLiters)
        );

        var withTerms = await SendAsync(
            admin,
            HttpMethod.Post,
            "/api/admin/rewards",
            new
            {
                name,
                pointsCost = 150,
                kind = "Product",
                discountPerLiter = 0.2m,
                isActive = true,
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, withTerms.StatusCode);
        Assert.Equal(
            "rewards.not_for_product",
            (await withTerms.Content.ReadFromJsonAsync<ProblemBody>())!
                .Errors!["discountPerLiter"][0]
                .Code
        );
        var fuelWithout = await SendAsync(
            admin,
            HttpMethod.Post,
            "/api/admin/rewards",
            new
            {
                name,
                pointsCost = 150,
                isActive = true,
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, fuelWithout.StatusCode);
        var unknownKind = await SendAsync(
            admin,
            HttpMethod.Post,
            "/api/admin/rewards",
            new
            {
                name,
                pointsCost = 150,
                kind = "Gift",
                isActive = true,
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, unknownKind.StatusCode);

        var url = $"/api/admin/rewards/{offer.Id}";
        var toFuel = await SendAsync(
            admin,
            HttpMethod.Put,
            url,
            new
            {
                name,
                pointsCost = 150,
                kind = "FuelDiscount",
                discountPerLiter = 0.2m,
                maxLiters = 50m,
                isActive = true,
                version = 1,
            }
        );
        Assert.Equal(HttpStatusCode.BadRequest, toFuel.StatusCode);
        Assert.Equal(
            "rewards.kind_immutable",
            (await toFuel.Content.ReadFromJsonAsync<ProblemBody>())!.Errors!["kind"][0].Code
        );
        var garbage = await SendAsync(
            admin,
            HttpMethod.Put,
            url,
            new
            {
                name,
                pointsCost = 150,
                kind = "Gift",
                isActive = true,
                version = 1,
            }
        );
        Assert.Equal(
            "rewards.invalid_format",
            (await garbage.Content.ReadFromJsonAsync<ProblemBody>())!.Errors!["kind"][0].Code
        );
        var edit = await SendAsync(
            admin,
            HttpMethod.Put,
            url,
            new
            {
                name,
                pointsCost = 200,
                isActive = true,
                version = 1,
            }
        );
        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);
        Assert.Equal("Product", (await edit.Content.ReadFromJsonAsync<OfferBody>())!.Kind);
    }

    [Fact]
    public async Task A_product_is_exchanged_activated_and_handed_over_once_without_a_purchase()
    {
        var customer = await CustomerAsync(balance: 1000);
        var cashierEmail = NewEmail("cashier");
        var cashier = await factory.CreateCashierAsync(cashierEmail);
        var offer = await OfferAsync(RewardKind.Product);
        var coupon = await ActiveCouponAsync(customer, offer);
        Assert.Equal(
            ("Product", offer.Name, 150),
            (coupon.Kind, coupon.OfferName, coupon.PointsCost)
        );

        // The snapshot keeps name and cost after the offer changes.
        await DbAsync(db =>
            db.RewardOffers.Where(o => o.Id == offer.Id)
                .ExecuteUpdateAsync(set =>
                    set.SetProperty(o => o.Name, "Inna nazwa " + _tag)
                        .SetProperty(o => o.PointsCost, 999)
                )
        );
        var ledgerBefore = await DbAsync(db =>
            db.PointsLedgerEntries.CountAsync(e => e.UserId == customer.UserId)
        );

        var key = Guid.NewGuid().ToString();
        var first = await HandOverAsync(cashier, customer.CardCode, coupon.Code!, key);
        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(
            offer.Name,
            (await first.Content.ReadFromJsonAsync<HandoverBody>())!.OfferName
        );
        var replay = await HandOverAsync(cashier, customer.CardCode, coupon.Code!, key);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        var again = await HandOverAsync(cashier, customer.CardCode, coupon.Code!);
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("rewards.coupon_redeemed", await CodeOf(again));

        var stored = await DbAsync(db =>
            db.Coupons.AsNoTracking().SingleAsync(c => c.Id == coupon.Id)
        );
        Assert.Equal(CouponStatus.Redeemed, stored.Status);
        Assert.Null(stored.RedeemedPurchaseId);
        Assert.Equal((offer.Name, 150), (stored.OfferName, stored.PointsCost));
        var handover = await DbAsync(db =>
            db.RewardHandovers.AsNoTracking().SingleAsync(h => h.CouponId == coupon.Id)
        );
        var cashierId = await DbAsync(db =>
            db.Users.Where(u => u.Email == cashierEmail).Select(u => u.Id).SingleAsync()
        );
        Assert.Equal(stored.RedeemedHandoverId, handover.Id);
        Assert.Equal((customer.UserId, cashierId), (handover.UserId, handover.CashierUserId));
        Assert.False(string.IsNullOrEmpty(handover.StationCode));
        Assert.False(string.IsNullOrEmpty(handover.RegisterCode));
        // No purchase, no points.
        Assert.Equal(
            0,
            await DbAsync(db => db.FuelPurchases.CountAsync(p => p.UserId == customer.UserId))
        );
        Assert.Equal(
            ledgerBefore,
            await DbAsync(db => db.PointsLedgerEntries.CountAsync(e => e.UserId == customer.UserId))
        );
    }

    [Fact]
    public async Task Kinds_do_not_mix_and_someone_elses_code_is_a_plain_404()
    {
        var customer = await CustomerAsync();
        var other = await CustomerAsync();
        var cashier = await factory.CreateCashierAsync(NewEmail("cashier"));
        var product = await ActiveCouponAsync(customer, await OfferAsync(RewardKind.Product));
        var fuel = await ActiveCouponAsync(
            customer,
            await OfferAsync(RewardKind.FuelDiscount, "Rabat")
        );
        var foreignFuel = await ActiveCouponAsync(
            other,
            await OfferAsync(RewardKind.FuelDiscount, "Obcy")
        );

        var fuelAtHandover = await HandOverAsync(cashier, customer.CardCode, fuel.Code!);
        Assert.Equal("rewards.coupon_wrong_kind", await CodeOf(fuelAtHandover));
        var productAtPurchase = await cashier.PurchaseAsync(
            CashierTestHelpers.Sale(customer.CardCode, product.Code)
        );
        Assert.Equal(HttpStatusCode.Conflict, productAtPurchase.StatusCode);
        Assert.Equal("rewards.coupon_wrong_kind", await CodeOf(productAtPurchase));
        Assert.Equal(
            0,
            await DbAsync(db => db.FuelPurchases.CountAsync(p => p.UserId == customer.UserId))
        );

        // Owner first: another customer's coupon is "not found", never "wrong kind".
        var foreign = await HandOverAsync(cashier, customer.CardCode, foreignFuel.Code!);
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);
        Assert.Equal("rewards.coupon_not_found", await CodeOf(foreign));
        var unknown = await HandOverAsync(cashier, customer.CardCode, CouponCode.Generate());
        Assert.Equal("rewards.coupon_not_found", await CodeOf(unknown));

        var preview = await SendAsync(
            cashier,
            HttpMethod.Post,
            "/api/cashier/coupons/preview",
            new { cardCode = customer.CardCode, couponCode = product.Code }
        );
        Assert.Equal(HttpStatusCode.OK, preview.StatusCode);
        var body = (await preview.Content.ReadFromJsonAsync<PreviewBody>())!;
        Assert.Equal(("Product", (object?)null), (body.Kind, body.Calculation));
        var priced = await SendAsync(
            cashier,
            HttpMethod.Post,
            "/api/cashier/coupons/preview",
            new
            {
                cardCode = customer.CardCode,
                couponCode = product.Code,
                amount = 100m,
                liters = 40m,
            }
        );
        Assert.Equal("rewards.coupon_wrong_kind", await CodeOf(priced));

        // Handed over: without amounts the preview says redeemed, with amounts the kind comes first
        // - the same order as the purchase.
        Assert.Equal(
            HttpStatusCode.OK,
            (await HandOverAsync(cashier, customer.CardCode, product.Code!)).StatusCode
        );
        var redeemed = await SendAsync(
            cashier,
            HttpMethod.Post,
            "/api/cashier/coupons/preview",
            new { cardCode = customer.CardCode, couponCode = product.Code }
        );
        Assert.Equal("rewards.coupon_redeemed", await CodeOf(redeemed));
        var redeemedPriced = await SendAsync(
            cashier,
            HttpMethod.Post,
            "/api/cashier/coupons/preview",
            new
            {
                cardCode = customer.CardCode,
                couponCode = product.Code,
                amount = 100m,
                liters = 40m,
            }
        );
        Assert.Equal("rewards.coupon_wrong_kind", await CodeOf(redeemedPriced));
        var redeemedPurchase = await cashier.PurchaseAsync(
            CashierTestHelpers.Sale(customer.CardCode, product.Code)
        );
        Assert.Equal("rewards.coupon_wrong_kind", await CodeOf(redeemedPurchase));

        var asCustomer = await HandOverAsync(customer.Client, customer.CardCode, product.Code!);
        Assert.Equal(HttpStatusCode.Forbidden, asCustomer.StatusCode);
    }

    [Fact]
    public async Task Parallel_handovers_with_different_keys_hand_over_once()
    {
        var customer = await CustomerAsync();
        var first = await factory.CreateCashierAsync(NewEmail("cashier"));
        var second = await factory.CreateCashierAsync(NewEmail("cashier"));
        var coupon = await ActiveCouponAsync(customer, await OfferAsync(RewardKind.Product));

        var results = await Task.WhenAll(
            HandOverAsync(first, customer.CardCode, coupon.Code!),
            HandOverAsync(second, customer.CardCode, coupon.Code!)
        );

        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        Assert.Single(
            results,
            r => r.StatusCode is HttpStatusCode.Conflict or HttpStatusCode.ServiceUnavailable
        );
        Assert.Equal(
            1,
            await DbAsync(db => db.RewardHandovers.CountAsync(h => h.CouponId == coupon.Id))
        );
    }

    [Fact]
    public async Task The_database_keeps_kinds_and_redemptions_apart()
    {
        var customer = await CustomerAsync();
        var product = await OfferAsync(RewardKind.Product);

        async Task Rejects(Func<AppDbContext, Task> change)
        {
            var error = await Assert.ThrowsAnyAsync<Exception>(() =>
                DbAsync(async db =>
                {
                    await change(db);
                    return 0;
                })
            );
            Assert.IsType<PostgresException>(error.InnerException ?? error);
        }

        // A product offer with per-liter terms.
        await Rejects(db =>
            db.RewardOffers.Where(o => o.Id == product.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(o => o.DiscountPerLiter, 0.2m))
        );
        // A coupon whose kind differs from its offer's.
        await Rejects(async db =>
        {
            db.Coupons.Add(
                new Coupon
                {
                    Id = Guid.CreateVersion7(),
                    UserId = customer.UserId,
                    OfferId = product.Id,
                    OfferName = product.Name,
                    PointsCost = 150,
                    Kind = RewardKind.FuelDiscount,
                    DiscountPerLiter = 0.2m,
                    MaxLiters = 50m,
                    Status = CouponStatus.Purchased,
                }
            );
            await db.SaveChangesAsync();
        });
        // A redeemed product without a handover (e.g. pointing at a purchase instead).
        var coupon = await ActiveCouponAsync(customer, product);
        await Rejects(db =>
            db.Coupons.Where(c => c.Id == coupon.Id)
                .ExecuteUpdateAsync(set =>
                    set.SetProperty(c => c.Status, CouponStatus.Redeemed)
                        .SetProperty(c => c.RedeemedAt, DateTimeOffset.UtcNow)
                )
        );
    }
}
