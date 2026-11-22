using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Loyalty;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Rewards.Cashier;

/// <summary>Codes go in the body, never in the URL. Amount and liters come together or not at all.</summary>
public sealed record CouponPreviewRequest(
    string? CardCode,
    string? CouponCode,
    decimal? Amount,
    decimal? Liters
);

/// <summary>Same formulas as the purchase; the purchase recomputes everything when it is saved.</summary>
public sealed record CouponPreviewCalculation(
    decimal Discount,
    decimal PaidAmount,
    int PointsAwarded
);

/// <summary>
/// The coupon's terms; no customer data. <c>Calculation</c> is null without amount and liters, and
/// always for a product (Kind = Product: the till hands it over instead of a purchase).
/// </summary>
public sealed record CouponPreviewResponse(
    string OfferName,
    string Kind,
    decimal? DiscountPerLiter,
    decimal? MaxLiters,
    CouponPreviewCalculation? Calculation
);

/// <summary>
/// Read-only: no lock, nothing reserved or changed. Order: validation (400) -> active card (404) ->
/// coupon of the card owner (404, the same for someone else's coupon, so a code does not reveal its
/// owner) -> with amount and liters, a product reward (409 wrong kind, the same order as the purchase)
/// -> redeemed (409). A purchased coupon has no code, so it cannot be found by one.
/// </summary>
public static class CouponPreviewEndpoint
{
    public const string RateLimitPolicy = "coupon-preview";

    public const string Description =
        "Shows a coupon's kind and discount for the scanned card. Without amount and liters returns the terms only; "
        + "a product reward has kind Product and null terms, and amount with liters for it gives 409 rewards.coupon_wrong_kind; "
        + "a handed-over one gives 409 rewards.coupon_redeemed with handedOverAt and stationCode. "
        + "Changes and reserves nothing: the purchase checks the coupon again. Limited per cashier (429).";

    public static async Task<Results<Ok<CouponPreviewResponse>, ProblemHttpResult>> Handle(
        CouponPreviewRequest request,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var errors = new List<IFieldError>();
        var cardCode = LoyaltyCardCode.Normalize(request.CardCode);
        if (string.IsNullOrWhiteSpace(request.CardCode))
            errors.Add(new LoyaltyValidationError("cardCode", LoyaltyErrorCodes.RequiredField));
        else if (!LoyaltyCardCode.IsValid(cardCode))
            errors.Add(new LoyaltyValidationError("cardCode", LoyaltyErrorCodes.InvalidFormat));

        var couponCode = CouponCode.Normalize(request.CouponCode);
        if (string.IsNullOrWhiteSpace(request.CouponCode))
            errors.Add(new RewardsValidationError("couponCode", RewardsErrorCodes.RequiredField));
        else if (!CouponCode.IsValid(couponCode))
            errors.Add(new RewardsValidationError("couponCode", RewardsErrorCodes.InvalidFormat));

        switch (request)
        {
            case { Amount: { } amount, Liters: { } liters }:
                errors.AddRange(FuelPurchaseRules.ValidateQuantities(amount, liters));
                break;
            case { Amount: null, Liters: { } }:
                errors.Add(new RewardsValidationError("amount", RewardsErrorCodes.RequiredField));
                break;
            case { Amount: { }, Liters: null }:
                errors.Add(new RewardsValidationError("liters", RewardsErrorCodes.RequiredField));
                break;
        }

        if (errors.Count > 0)
            return ApiProblems.Validation(
                "Coupon preview is not valid",
                RewardsErrorCodes.ValidationFailed,
                errors
            );

        var ownerId = await db
            .LoyaltyCards.AsNoTracking()
            .Where(card => card.Code == cardCode && card.RevokedAt == null)
            .Select(card => card.UserId)
            .SingleOrDefaultAsync(cancellationToken);
        if (ownerId is null)
            return LoyaltyProblems.CardNotFound();

        var coupon = await db
            .Coupons.AsNoTracking()
            .SingleOrDefaultAsync(
                c => c.Code == couponCode && c.UserId == ownerId,
                cancellationToken
            );
        if (coupon is null)
            return RewardsProblems.CouponNotFound();
        var priced = request is { Amount: not null, Liters: not null };
        if (priced && coupon.Kind != RewardKind.FuelDiscount)
            return RewardsProblems.CouponWrongKind();
        if (coupon.Status == CouponStatus.Redeemed)
            return coupon.Kind == RewardKind.Product
                ? RewardsProblems.RewardHandedOver(
                    await db
                        .RewardHandovers.AsNoTracking()
                        .SingleOrDefaultAsync(
                            h => h.CouponId == coupon.Id && h.UserId == ownerId,
                            cancellationToken
                        )
                )
                : RewardsProblems.CouponRedeemed();

        CouponPreviewCalculation? calculation = null;
        if (request is { Amount: { } gross, Liters: { } litres })
        {
            var result = CouponDiscount.Calculate(
                coupon.DiscountPerLiter!.Value,
                coupon.MaxLiters!.Value,
                litres,
                gross
            );
            calculation = new(
                result.Discount,
                result.PaidAmount,
                PointsPolicy.ForPaidAmount(result.PaidAmount)
            );
        }

        return TypedResults.Ok(
            new CouponPreviewResponse(
                coupon.OfferName,
                coupon.Kind.ToString(),
                coupon.DiscountPerLiter,
                coupon.MaxLiters,
                calculation
            )
        );
    }
}
