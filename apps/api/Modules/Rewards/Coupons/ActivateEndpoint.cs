using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Rewards.Coupons;

/// <summary>
/// Purchased -> Active with a new redemption code. No points move. The conditional UPDATE allows only
/// that transition, so a redeemed coupon never becomes active again and parallel activations produce
/// one code. When it changes nothing, the current state decides: missing or someone else's -> 404,
/// already active -> 200 with the same code, redeemed -> 409.
/// </summary>
public static class ActivateEndpoint
{
    public const string Description =
        "Activates an own coupon and returns its redemption code. Activating an active coupon returns the "
        + "same code. A retry with the same Idempotency-Key replays the stored response even if the coupon "
        + "was redeemed since: read the current state with GET.";

    public static async Task<Results<Ok<CouponResponse>, ProblemHttpResult>> Handle(
        Guid id,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var userId = users.GetUserId(http.User)!;

        // clock_timestamp(), not now(): now() is the start of the idempotency transaction.
        // The code is a parameter, so logged SQL never contains it.
        var code = CouponCode.Generate();
        var activated = await db
            .Coupons.FromSql(
                $"""
                UPDATE "Coupons"
                SET "Status" = 'Active', "Code" = {code}, "ActivatedAt" = clock_timestamp()
                WHERE "Id" = {id} AND "UserId" = {userId} AND "Status" = 'Purchased'
                RETURNING *
                """
            )
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        if (activated.SingleOrDefault() is { } coupon)
            return TypedResults.Ok(MyCouponsEndpoints.ToResponse(coupon));

        var current = await MyCouponsEndpoints
            .Own(db, userId)
            .SingleOrDefaultAsync(c => c.Id == id, cancellationToken);
        return current?.Status switch
        {
            null => RewardsProblems.CouponNotFound(),
            CouponStatus.Active => TypedResults.Ok(MyCouponsEndpoints.ToResponse(current)),
            CouponStatus.Redeemed => RewardsProblems.CouponRedeemed(),
            _ => throw new InvalidOperationException("A purchased coupon was not activated."),
        };
    }
}
