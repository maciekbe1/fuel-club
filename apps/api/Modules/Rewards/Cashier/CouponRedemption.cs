using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Loyalty.Cashier;
using FuelClub.Api.Modules.Rewards.Domain;
using FuelClub.Api.Modules.Rewards.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FuelClub.Api.Modules.Rewards.Cashier;

/// <summary>
/// Scoped, so it shares the request's AppDbContext with the purchase. The caller holds the customer
/// lock, so other purchases of the owner are serialized. An uncommitted activation is not matched (its
/// code is not visible yet), so the bounded row-lock wait only covers a lock held by some other
/// transaction. A hold is remembered with its transaction and redemption checks both.
/// </summary>
public sealed class CouponRedemption(AppDbContext db, IOptions<RewardsOptions> options)
    : ICouponRedemption
{
    private readonly Dictionary<Guid, IDbContextTransaction> _holds = [];

    public IFieldError? Validate(string field, string code, out string normalized)
    {
        normalized = CouponCode.Normalize(code);
        return CouponCode.IsValid(normalized)
            ? null
            : new RewardsValidationError(field, RewardsErrorCodes.InvalidFormat);
    }

    public async Task<CouponHoldResult> HoldAsync(
        HttpContext http,
        string ownerId,
        string code,
        decimal liters,
        decimal grossAmount,
        CancellationToken cancellationToken
    )
    {
        var transaction =
            db.Database.CurrentTransaction
            ?? throw new InvalidOperationException(
                "A coupon hold is transaction-scoped; call it inside a transaction."
            );

        var timeoutMs = (long)options.Value.CouponLockTimeout.TotalMilliseconds;
        await db.Database.ExecuteSqlRawAsync(
            $"SET LOCAL lock_timeout = {timeoutMs}",
            cancellationToken
        );
        List<Coupon> coupons;
        try
        {
            // Someone else's coupon is not matched, so it looks exactly like a missing one.
            coupons = await db
                .Coupons.FromSql(
                    $"""
                    SELECT * FROM "Coupons"
                    WHERE "Code" = {code} AND "UserId" = {ownerId}
                    FOR UPDATE
                    """
                )
                .AsNoTracking()
                .ToListAsync(cancellationToken);
        }
        // Queries go through the execution strategy, which wraps errors it considers transient.
        catch (Exception exception)
            when ((exception as PostgresException ?? exception.InnerException as PostgresException)
                    is { SqlState: PostgresErrorCodes.LockNotAvailable }
            )
        {
            return new CouponHoldResult(null, RewardsProblems.CouponBusy(http));
        }
        await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL lock_timeout TO DEFAULT",
            cancellationToken
        );

        if (coupons.SingleOrDefault() is not { } coupon)
            return new CouponHoldResult(null, RewardsProblems.CouponNotFound());
        if (coupon.Status == CouponStatus.Redeemed)
            return new CouponHoldResult(null, RewardsProblems.CouponRedeemed());
        // Only an active coupon is usable; any other state with a code is treated as missing.
        if (coupon.Status != CouponStatus.Active)
            return new CouponHoldResult(null, RewardsProblems.CouponNotFound());

        var result = CouponDiscount.Calculate(
            coupon.DiscountPerLiter,
            coupon.MaxLiters,
            liters,
            grossAmount
        );
        _holds[coupon.Id] = transaction;
        return new CouponHoldResult(
            new CouponHold(coupon.Id, result.Discount, result.PaidAmount),
            null
        );
    }

    public async Task MarkRedeemedAsync(
        Guid couponId,
        string ownerId,
        Guid purchaseId,
        CancellationToken cancellationToken
    )
    {
        if (
            !_holds.Remove(couponId, out var transaction)
            || transaction != db.Database.CurrentTransaction
        )
            throw new InvalidOperationException(
                "The coupon was not held in the current transaction."
            );

        // clock_timestamp(), not now(): now() is the transaction start, which can precede an activation
        // committed while this purchase waited for the customer lock.
        var updated = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "Coupons"
            SET "Status" = 'Redeemed', "RedeemedAt" = clock_timestamp(), "RedeemedPurchaseId" = {purchaseId}
            WHERE "Id" = {couponId} AND "UserId" = {ownerId} AND "Status" = 'Active'
            """,
            cancellationToken
        );
        if (updated != 1)
            throw new InvalidOperationException("The held coupon was not active.");
    }
}
