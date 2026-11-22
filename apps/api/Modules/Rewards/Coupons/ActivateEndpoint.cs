using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Rewards.Coupons;

/// <summary>HTTP entry to <see cref="CouponActivation"/>: 404 missing or someone else's, 409 redeemed.</summary>
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
        var result = await CouponActivation.ActivateAsync(db, userId, id, cancellationToken);
        return CouponActivation.ToHttp(result);
    }
}
