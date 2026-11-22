using FuelClub.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FuelClub.Api.Modules.Loyalty.Cashier;

/// <summary>A coupon locked for this purchase, with the discount computed from its snapshot.</summary>
public sealed record CouponHold(Guid CouponId, decimal Discount, decimal PaidAmount);

/// <summary>Either a hold or the problem to return (not found, redeemed, busy).</summary>
public sealed record CouponHoldResult(CouponHold? Hold, ProblemHttpResult? Problem);

/// <summary>
/// Implemented by Rewards, so Loyalty does not depend on the coupon model. Both calls run on the
/// request's AppDbContext and inside the same transaction as the purchase: the hold locks the coupon
/// row until that transaction ends, and the redemption is rejected outside the transaction of its hold.
/// </summary>
public interface ICouponRedemption
{
    /// <summary>Normalizes a typed code; returns a field error when it is blank or malformed.</summary>
    IFieldError? Validate(string field, string code, out string normalized);

    /// <summary>Locks the owner's coupon with this code (bounded wait) and computes the discount.</summary>
    Task<CouponHoldResult> HoldAsync(
        HttpContext http,
        string ownerId,
        string code,
        decimal liters,
        decimal grossAmount,
        CancellationToken cancellationToken
    );

    /// <summary>Active -> Redeemed for the held coupon of this owner; throws when nothing changed.</summary>
    Task MarkRedeemedAsync(
        Guid couponId,
        string ownerId,
        Guid purchaseId,
        CancellationToken cancellationToken
    );
}
