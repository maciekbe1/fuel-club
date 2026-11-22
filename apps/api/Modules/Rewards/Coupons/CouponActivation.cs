using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Rewards.Coupons;

public enum ActivationOutcome
{
    Activated,
    AlreadyActive,
    NotFound,
    Redeemed,
}

public sealed record ActivationResult(ActivationOutcome Outcome, Coupon? Coupon);

/// <summary>
/// Purchased -> Active with a new redemption code; shared by the coupon endpoint and the chat
/// confirmation. No points move. The conditional UPDATE allows only that transition, so a redeemed
/// coupon never becomes active again and parallel activations produce one code. When it changes
/// nothing, the current state decides: missing or someone else's -> NotFound, already active ->
/// AlreadyActive with the same code, redeemed -> Redeemed.
/// </summary>
public static class CouponActivation
{
    public static async Task<ActivationResult> ActivateAsync(
        AppDbContext db,
        string userId,
        Guid couponId,
        CancellationToken cancellationToken
    )
    {
        // clock_timestamp(), not now(): now() is the start of the idempotency transaction.
        // The code is a parameter, so logged SQL never contains it.
        var code = CouponCode.Generate();
        var activated = await db
            .Coupons.FromSql(
                $"""
                UPDATE "Coupons"
                SET "Status" = 'Active', "Code" = {code}, "ActivatedAt" = clock_timestamp()
                WHERE "Id" = {couponId} AND "UserId" = {userId} AND "Status" = 'Purchased'
                RETURNING *
                """
            )
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (activated.SingleOrDefault() is { } coupon)
            return new(ActivationOutcome.Activated, coupon);

        var current = await MyCouponsEndpoints
            .Own(db, userId)
            .SingleOrDefaultAsync(c => c.Id == couponId, cancellationToken);
        return current?.Status switch
        {
            null => new(ActivationOutcome.NotFound, null),
            CouponStatus.Active => new(ActivationOutcome.AlreadyActive, current),
            CouponStatus.Redeemed => new(ActivationOutcome.Redeemed, current),
            _ => throw new InvalidOperationException("A purchased coupon was not activated."),
        };
    }

    /// <summary>The HTTP answer shared by both endpoints.</summary>
    public static Results<Ok<CouponResponse>, ProblemHttpResult> ToHttp(ActivationResult result) =>
        result.Outcome switch
        {
            ActivationOutcome.Activated or ActivationOutcome.AlreadyActive => TypedResults.Ok(
                MyCouponsEndpoints.ToResponse(result.Coupon!)
            ),
            ActivationOutcome.Redeemed => RewardsProblems.CouponRedeemed(),
            _ => RewardsProblems.CouponNotFound(),
        };
}
