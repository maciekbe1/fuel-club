using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Rewards.Coupons;

/// <summary>The offer snapshot taken at the exchange. <c>Code</c> is null until activation; shown only to the owner.</summary>
public sealed record CouponResponse(
    Guid Id,
    Guid OfferId,
    string OfferName,
    int PointsCost,
    string Kind,
    decimal? DiscountPerLiter,
    decimal? MaxLiters,
    string Status,
    string? Code,
    DateTimeOffset PurchasedAt,
    DateTimeOffset? ActivatedAt,
    DateTimeOffset? RedeemedAt
);

public sealed record CouponsResponse(IReadOnlyList<CouponResponse> Items);

/// <summary>Views of the logged-in customer's own coupons. No user parameter exists.</summary>
public static class MyCouponsEndpoints
{
    public const string Path = "/api/me/coupons";

    public static async Task<Ok<CouponsResponse>> GetAll(
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var userId = users.GetUserId(http.User)!;
        var items = await Own(db, userId)
            .OrderByDescending(coupon => coupon.PurchasedAt)
            .ThenByDescending(coupon => coupon.Id)
            .Select(Projection)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new CouponsResponse(items));
    }

    public static async Task<Results<Ok<CouponResponse>, ProblemHttpResult>> GetOne(
        Guid id,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var userId = users.GetUserId(http.User)!;
        var coupon = await Own(db, userId)
            .Where(coupon => coupon.Id == id)
            .Select(Projection)
            .SingleOrDefaultAsync(cancellationToken);
        return coupon is null ? RewardsProblems.CouponNotFound() : TypedResults.Ok(coupon);
    }

    internal static IQueryable<Coupon> Own(AppDbContext db, string userId) =>
        db.Coupons.AsNoTracking().Where(coupon => coupon.UserId == userId);

    public static CouponResponse ToResponse(Coupon coupon) =>
        new(
            coupon.Id,
            coupon.OfferId,
            coupon.OfferName,
            coupon.PointsCost,
            coupon.Kind.ToString(),
            coupon.DiscountPerLiter,
            coupon.MaxLiters,
            coupon.Status.ToString(),
            coupon.Code,
            coupon.PurchasedAt,
            coupon.ActivatedAt,
            coupon.RedeemedAt
        );

    private static readonly System.Linq.Expressions.Expression<
        Func<Coupon, CouponResponse>
    > Projection = coupon => new CouponResponse(
        coupon.Id,
        coupon.OfferId,
        coupon.OfferName,
        coupon.PointsCost,
        coupon.Kind.ToString(),
        coupon.DiscountPerLiter,
        coupon.MaxLiters,
        coupon.Status.ToString(),
        coupon.Code,
        coupon.PurchasedAt,
        coupon.ActivatedAt,
        coupon.RedeemedAt
    );
}
