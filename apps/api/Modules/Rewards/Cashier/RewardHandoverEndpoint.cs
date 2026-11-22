using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Rewards.Cashier;

/// <summary>Codes in the body only, never in the URL or logs.</summary>
public sealed record RewardHandoverRequest(string? CardCode, string? CouponCode);

public sealed record RewardHandoverResponse(
    Guid CouponId,
    string OfferName,
    DateTimeOffset HandedOverAt
);

/// <summary>
/// Hands over a product reward at the till (ADR 0013): no fuel purchase, no points. Order:
/// validation (400) -> station (404) -> active card (404) -> customer lock -> coupon of the card
/// owner (404, also for someone else's) -> kind (409 wrong kind) -> redeemed (409) -> handover.
/// The customer lock serializes it with the owner's purchases and other handovers; the conditional
/// UPDATE is the last line against a second handover.
/// </summary>
public static class RewardHandoverEndpoint
{
    public const string Description =
        "Hands over an active product reward of the scanned card's owner. Records the cashier, station, register "
        + "and time; registers no purchase and awards no points. A fuel discount coupon returns 409 "
        + "rewards.coupon_wrong_kind. Retry with the same Idempotency-Key replays the result.";

    public static async Task<Results<Ok<RewardHandoverResponse>, ProblemHttpResult>> Handle(
        RewardHandoverRequest request,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CustomerLock customerLock,
        CancellationToken cancellationToken
    )
    {
        var cashierId = users.GetUserId(http.User)!;
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
        if (errors.Count > 0)
            return ApiProblems.Validation(
                "Reward handover is not valid",
                RewardsErrorCodes.ValidationFailed,
                errors
            );

        var station = await db
            .CashierStations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.UserId == cashierId, cancellationToken);
        if (station is null)
            return ApiProblems.Problem(
                StatusCodes.Status404NotFound,
                "Cashier station not assigned",
                LoyaltyErrorCodes.CashierStationMissing
            );

        var card = await db
            .LoyaltyCards.AsNoTracking()
            .Where(c => c.Code == cardCode && c.RevokedAt == null)
            .Select(c => new { c.Id, c.UserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (card is null)
            return LoyaltyProblems.CardNotFound();

        if (!await customerLock.TryAcquireAsync(card.UserId, cancellationToken))
            return LoyaltyProblems.CustomerBusy(http);
        // A new statement after the lock sees rotations committed while this request waited.
        if (
            !await db.LoyaltyCards.AnyAsync(
                c => c.Id == card.Id && c.RevokedAt == null,
                cancellationToken
            )
        )
            return LoyaltyProblems.CardNotFound();

        // Someone else's coupon is not matched, so it looks exactly like a missing one.
        var coupon = await db
            .Coupons.AsNoTracking()
            .Where(c => c.Code == couponCode && c.UserId == card.UserId)
            .Select(c => new
            {
                c.Id,
                c.OfferName,
                c.Kind,
                c.Status,
            })
            .SingleOrDefaultAsync(cancellationToken);
        if (coupon is null)
            return RewardsProblems.CouponNotFound();
        if (coupon.Kind != RewardKind.Product)
            return RewardsProblems.CouponWrongKind();
        if (coupon.Status == CouponStatus.Redeemed)
            return RewardsProblems.CouponRedeemed();
        if (coupon.Status != CouponStatus.Active)
            return RewardsProblems.CouponNotFound();

        var handoverId = Guid.CreateVersion7();
        db.RewardHandovers.Add(
            new RewardHandover
            {
                Id = handoverId,
                CouponId = coupon.Id,
                UserId = card.UserId,
                CashierUserId = cashierId,
                StationCode = station.StationCode,
                RegisterCode = station.RegisterCode,
            }
        );
        await db.SaveChangesAsync(cancellationToken);

        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Coupons"
            SET "Status" = 'Redeemed', "RedeemedAt" = clock_timestamp(), "RedeemedHandoverId" = {handoverId}
            WHERE "Id" = {coupon.Id} AND "UserId" = {card.UserId} AND "Status" = 'Active' AND "Kind" = 'Product'
            """,
            cancellationToken
        );
        // Under the customer lock nothing else can redeem it; a miss is a bug, not a user error.
        if (updated != 1)
            throw new InvalidOperationException("The product coupon was not active.");

        var handedOverAt = await db
            .RewardHandovers.Where(h => h.Id == handoverId)
            .Select(h => h.HandedOverAt)
            .SingleAsync(cancellationToken);
        return TypedResults.Ok(
            new RewardHandoverResponse(coupon.Id, coupon.OfferName, handedOverAt)
        );
    }
}
