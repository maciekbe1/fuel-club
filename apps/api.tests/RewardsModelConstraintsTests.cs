using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace FuelClub.Api.Tests;

/// <summary>Rewards constraints, written directly through EF Core past any application validation.</summary>
[Collection(DatabaseCollection.Name)]
public sealed class RewardsModelConstraintsTests(ApiFactory factory) : IAsyncLifetime
{
    private const string Password = "Valid-Test-Password1!";
    private readonly string _userEmail = $"rewards-user-{Guid.NewGuid():N}@example.test";
    private readonly List<Guid> _offerIds = [];
    private string _userId = null!;

    public async Task InitializeAsync()
    {
        await factory.CreateTestUserAsync(_userEmail, Password, "User");
        _userId = await DbAsync(db =>
            db.Users.Where(user => user.Email == _userEmail).Select(user => user.Id).SingleAsync()
        );
    }

    public async Task DisposeAsync()
    {
        await factory.DeleteTestUserAsync(_userEmail);
        await DbAsync(db =>
            db.RewardOffers.Where(offer => _offerIds.Contains(offer.Id)).ExecuteDeleteAsync()
        );
    }

    private async Task<T> DbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    private RewardOffer Offer(
        decimal rate = 0.20m,
        decimal maxLiters = 50m,
        int cost = 500,
        string name = "Rabat"
    )
    {
        var offer = new RewardOffer
        {
            Id = Guid.CreateVersion7(),
            Name = name,
            Description = "",
            PointsCost = cost,
            DiscountPerLiter = rate,
            MaxLiters = maxLiters,
            IsActive = true,
        };
        _offerIds.Add(offer.Id);
        return offer;
    }

    private Coupon NewCoupon(
        RewardOffer offer,
        CouponStatus status = CouponStatus.Purchased,
        string? code = null,
        DateTimeOffset? activatedAt = null,
        DateTimeOffset? redeemedAt = null,
        Guid? purchaseId = null
    ) =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = _userId,
            OfferId = offer.Id,
            OfferName = offer.Name,
            PointsCost = offer.PointsCost,
            DiscountPerLiter = offer.DiscountPerLiter,
            MaxLiters = offer.MaxLiters,
            Status = status,
            Code = code,
            PurchasedAt = DateTimeOffset.UtcNow.AddMinutes(-10),
            ActivatedAt = activatedAt,
            RedeemedAt = redeemedAt,
            RedeemedPurchaseId = purchaseId,
        };

    private Coupon Active(RewardOffer offer) =>
        NewCoupon(
            offer,
            CouponStatus.Active,
            CouponCode.Generate(),
            DateTimeOffset.UtcNow.AddMinutes(-5)
        );

    private Coupon Redeemed(RewardOffer offer, Guid purchaseId) =>
        NewCoupon(
            offer,
            CouponStatus.Redeemed,
            CouponCode.Generate(),
            DateTimeOffset.UtcNow.AddMinutes(-5),
            DateTimeOffset.UtcNow,
            purchaseId
        );

    private FuelPurchase Purchase() =>
        new()
        {
            Id = Guid.CreateVersion7(),
            UserId = _userId,
            RegisteredByUserId = _userId,
            StationCode = "ST-001",
            RegisterCode = "K1",
            SaleDate = new DateOnly(2026, 11, 21),
            ReceiptNumber = Guid.NewGuid().ToString("N").ToUpperInvariant(),
            GrossAmount = 100m,
            Discount = 8.10m,
            PaidAmount = 91.90m,
            Liters = 40.5m,
            PointsAwarded = 91,
        };

    private Task SaveAsync(params object[] entities) =>
        DbAsync(async db =>
        {
            db.AddRange(entities);
            return await db.SaveChangesAsync();
        });

    private async Task AssertRejectedAsync(
        string sqlState,
        string constraint,
        params object[] entities
    )
    {
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(entities));
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        Assert.Equal(sqlState, postgres.SqlState);
        Assert.Equal(constraint, postgres.ConstraintName);
    }

    [Fact]
    public async Task Accepts_coupons_in_every_consistent_state()
    {
        var offer = Offer();
        var purchase = Purchase();
        await SaveAsync(offer, purchase);
        await SaveAsync(NewCoupon(offer), Active(offer), Redeemed(offer, purchase.Id));
    }

    [Theory]
    [InlineData("0", "50", 500, "CK_RewardOffers_DiscountPerLiter_Range")]
    [InlineData("10.01", "50", 500, "CK_RewardOffers_DiscountPerLiter_Range")]
    [InlineData("0.20", "0", 500, "CK_RewardOffers_MaxLiters_Range")]
    [InlineData("0.20", "1000.001", 500, "CK_RewardOffers_MaxLiters_Range")]
    [InlineData("0.20", "50", 0, "CK_RewardOffers_PointsCost_Range")]
    public async Task Rejects_offer_benefit_outside_range(
        string rate,
        string liters,
        int cost,
        string constraint
    ) =>
        await AssertRejectedAsync(
            PostgresErrorCodes.CheckViolation,
            constraint,
            Offer(
                decimal.Parse(rate, System.Globalization.CultureInfo.InvariantCulture),
                decimal.Parse(liters, System.Globalization.CultureInfo.InvariantCulture),
                cost
            )
        );

    [Fact]
    public async Task Rejects_blank_offer_name() =>
        await AssertRejectedAsync(
            PostgresErrorCodes.CheckViolation,
            "CK_RewardOffers_Name_NotBlank",
            Offer(name: "  ")
        );

    /// <summary>Documents why precision is validated in the application: numeric(4,2) rounds, it does not reject.</summary>
    [Fact]
    public async Task Database_rounds_extra_decimals_instead_of_rejecting_them()
    {
        var offer = Offer(rate: 0.205m);
        await SaveAsync(offer);
        Assert.Equal(
            0.21m,
            await DbAsync(db =>
                db.RewardOffers.Where(o => o.Id == offer.Id)
                    .Select(o => o.DiscountPerLiter)
                    .SingleAsync()
            )
        );
    }

    [Fact]
    public async Task Rejects_coupon_benefit_snapshot_outside_range()
    {
        var offer = Offer();
        await SaveAsync(offer);
        var coupon = NewCoupon(offer);
        coupon.DiscountPerLiter = 0m;
        await AssertRejectedAsync(
            PostgresErrorCodes.CheckViolation,
            "CK_Coupons_DiscountPerLiter_Range",
            coupon
        );
    }

    public static TheoryData<string> InconsistentStates =>
        new()
        {
            "purchased-with-code",
            "purchased-activated",
            "active-without-code",
            "active-without-activation",
            "active-with-purchase",
            "redeemed-without-purchase",
            "redeemed-without-time",
        };

    [Theory]
    [MemberData(nameof(InconsistentStates))]
    public async Task Rejects_inconsistent_coupon_state(string state)
    {
        var offer = Offer();
        var purchase = Purchase();
        await SaveAsync(offer, purchase);
        var now = DateTimeOffset.UtcNow;
        var coupon = state switch
        {
            "purchased-with-code" => NewCoupon(offer, code: CouponCode.Generate()),
            "purchased-activated" => NewCoupon(offer, activatedAt: now),
            "active-without-code" => NewCoupon(offer, CouponStatus.Active, activatedAt: now),
            "active-without-activation" => NewCoupon(
                offer,
                CouponStatus.Active,
                CouponCode.Generate()
            ),
            "active-with-purchase" => NewCoupon(
                offer,
                CouponStatus.Active,
                CouponCode.Generate(),
                now,
                purchaseId: purchase.Id
            ),
            "redeemed-without-purchase" => NewCoupon(
                offer,
                CouponStatus.Redeemed,
                CouponCode.Generate(),
                now,
                now
            ),
            "redeemed-without-time" => NewCoupon(
                offer,
                CouponStatus.Redeemed,
                CouponCode.Generate(),
                now,
                purchaseId: purchase.Id
            ),
            _ => throw new ArgumentOutOfRangeException(nameof(state)),
        };

        await AssertRejectedAsync(PostgresErrorCodes.CheckViolation, "CK_Coupons_State", coupon);
    }

    [Fact]
    public async Task Rejects_activation_before_purchase()
    {
        var offer = Offer();
        await SaveAsync(offer);
        var coupon = Active(offer);
        coupon.ActivatedAt = coupon.PurchasedAt.AddSeconds(-1);
        await AssertRejectedAsync(PostgresErrorCodes.CheckViolation, "CK_Coupons_Timeline", coupon);
    }

    [Theory]
    [InlineData("K8000000000000000000000000")] // 26 chars, too short
    [InlineData("K80000000000000000000000000")] // first random char above 7
    [InlineData("70000000000000000000000000Z")] // card-like, no prefix
    [InlineData("k00000000000000000000000000")] // not normalized
    public async Task Rejects_coupon_code_in_another_format(string code)
    {
        var offer = Offer();
        await SaveAsync(offer);
        var coupon = Active(offer);
        coupon.Code = code;
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => SaveAsync(coupon));
        var postgres = Assert.IsType<PostgresException>(error.InnerException);
        // A 26-character value is padded by char(27) and still fails the format check.
        Assert.Equal("CK_Coupons_Code_Format", postgres.ConstraintName);
    }

    [Fact]
    public async Task Rejects_a_duplicate_code()
    {
        var offer = Offer();
        await SaveAsync(offer);
        var first = Active(offer);
        await SaveAsync(first);
        var second = Active(offer);
        second.Code = first.Code;
        await AssertRejectedAsync(PostgresErrorCodes.UniqueViolation, "UX_Coupons_Code", second);
    }

    [Fact]
    public async Task One_coupon_per_purchase()
    {
        var offer = Offer();
        var purchase = Purchase();
        await SaveAsync(offer, purchase);
        await SaveAsync(Redeemed(offer, purchase.Id));
        await AssertRejectedAsync(
            PostgresErrorCodes.UniqueViolation,
            "UX_Coupons_RedeemedPurchase",
            Redeemed(offer, purchase.Id)
        );
    }

    [Fact]
    public async Task A_coupon_cannot_be_redeemed_on_another_customers_purchase()
    {
        var otherEmail = $"rewards-other-{Guid.NewGuid():N}@example.test";
        await factory.CreateTestUserAsync(otherEmail, Password, "User");
        try
        {
            var otherId = await DbAsync(db =>
                db.Users.Where(user => user.Email == otherEmail)
                    .Select(user => user.Id)
                    .SingleAsync()
            );
            var offer = Offer();
            var purchase = Purchase();
            purchase.UserId = otherId;
            await SaveAsync(offer, purchase);

            await AssertRejectedAsync(
                PostgresErrorCodes.ForeignKeyViolation,
                "FK_Coupons_FuelPurchases_RedeemedPurchaseOwner",
                Redeemed(offer, purchase.Id)
            );
        }
        finally
        {
            await factory.DeleteTestUserAsync(otherEmail);
        }
    }

    [Fact]
    public async Task The_owner_migration_stops_on_existing_mismatches_without_changing_data()
    {
        var otherEmail = $"rewards-other-{Guid.NewGuid():N}@example.test";
        await factory.CreateTestUserAsync(otherEmail, Password, "User");
        try
        {
            await using var scope = factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            // Consistent data passes.
            await db.Database.ExecuteSqlRawAsync(
                Migrations.CouponRedeemedPurchaseOwner.OwnerCheckSql
            );

            var otherId = await db
                .Users.Where(user => user.Email == otherEmail)
                .Select(user => user.Id)
                .SingleAsync();
            await using var transaction = await db.Database.BeginTransactionAsync();
            // Recreate the state before the migration, then roll everything back.
            await db.Database.ExecuteSqlRawAsync(
                """ALTER TABLE "Coupons" DROP CONSTRAINT "FK_Coupons_FuelPurchases_RedeemedPurchaseOwner" """
            );
            var offer = Offer();
            var purchase = Purchase();
            purchase.UserId = otherId;
            var coupon = Redeemed(offer, purchase.Id);
            db.AddRange(offer, purchase, coupon);
            await db.SaveChangesAsync();

            var error = await Assert.ThrowsAsync<PostgresException>(() =>
                db.Database.ExecuteSqlRawAsync(Migrations.CouponRedeemedPurchaseOwner.OwnerCheckSql)
            );
            Assert.Contains(
                "1 coupon(s) redeemed on another customer's purchase",
                error.MessageText
            );
            await transaction.RollbackAsync();
        }
        finally
        {
            await factory.DeleteTestUserAsync(otherEmail);
        }
    }

    [Fact]
    public async Task An_offer_with_coupons_cannot_be_deleted()
    {
        var offer = Offer();
        await SaveAsync(offer);
        await SaveAsync(NewCoupon(offer));

        var error = await Assert.ThrowsAsync<PostgresException>(() =>
            DbAsync(db => db.RewardOffers.Where(o => o.Id == offer.Id).ExecuteDeleteAsync())
        );
        Assert.Equal(PostgresErrorCodes.ForeignKeyViolation, error.SqlState);
    }

    [Fact]
    public async Task Rejects_version_below_one()
    {
        var offer = Offer();
        offer.Version = 0;
        await AssertRejectedAsync(
            PostgresErrorCodes.CheckViolation,
            "CK_RewardOffers_Version_Positive",
            offer
        );
    }

    [Fact]
    public async Task Concurrent_ef_edits_are_detected_and_each_raises_the_version()
    {
        var offer = Offer();
        await SaveAsync(offer);

        await using var first = factory.Services.CreateAsyncScope();
        await using var second = factory.Services.CreateAsyncScope();
        var firstDb = first.ServiceProvider.GetRequiredService<AppDbContext>();
        var secondDb = second.ServiceProvider.GetRequiredService<AppDbContext>();
        var a = await firstDb.RewardOffers.SingleAsync(o => o.Id == offer.Id);
        var b = await secondDb.RewardOffers.SingleAsync(o => o.Id == offer.Id);

        a.PointsCost = 600;
        await firstDb.SaveChangesAsync();
        Assert.Equal(2, a.Version);

        b.PointsCost = 700;
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => secondDb.SaveChangesAsync());
    }

    [Fact]
    public async Task A_new_offer_starts_at_version_one_without_setting_it()
    {
        var offer = Offer();
        await SaveAsync(offer);
        await using var scope = factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(
            1,
            (await db.RewardOffers.AsNoTracking().SingleAsync(o => o.Id == offer.Id)).Version
        );
    }
}
