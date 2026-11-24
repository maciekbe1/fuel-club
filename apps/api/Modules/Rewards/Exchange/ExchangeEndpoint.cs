using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Loyalty.Points;
using FuelClub.Api.Modules.Rewards.Coupons;
using FuelClub.Api.Modules.Rewards.Domain;
using FuelClub.Api.Modules.Rewards.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FuelClub.Api.Modules.Rewards.Exchange;

/// <summary><c>Version</c> is the offer version from the catalog the customer saw.</summary>
public sealed record ExchangeRequest(int? Version);

public sealed record ExchangeResponse(CouponResponse Coupon, int Balance);

/// <summary>
/// Runs inside the idempotency transaction. Order: validation -> offer row (FOR SHARE, so an admin
/// edit waits until this commits; waiting for an edit is bounded by OfferLockTimeout, then 503) -> missing (404) -> inactive (409) -> other version (409) ->
/// customer lock and conditional debit (409 when short, 503 when busy) -> coupon with the offer
/// snapshot and the ledger entry, saved together. Inactive goes before the version check because
/// deactivation also raises the version.
/// </summary>
public static class ExchangeEndpoint
{
    public const string Description =
        "Buys a coupon with points. Send the offer version from the catalog: if the offer changed since, "
        + "nothing is charged (409 rewards.offer_changed) and the customer should see the new terms. "
        + "A stored 409 rewards.insufficient_points is replayed for the same Idempotency-Key, so a new "
        + "attempt after earning points needs a new key.";

    public static async Task<Results<Created<ExchangeResponse>, ProblemHttpResult>> Handle(
        Guid id,
        ExchangeRequest request,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        PointsSpending points,
        IOptions<RewardsOptions> options,
        CancellationToken cancellationToken
    )
    {
        if (request.Version is not { } version)
            return RewardsProblems.Validation(
                "Exchange is not valid",
                [new("version", RewardsErrorCodes.RequiredField)]
            );
        if (version < 1)
            return RewardsProblems.Validation(
                "Exchange is not valid",
                [new("version", RewardsErrorCodes.VersionOutOfRange)]
            );

        if (
            await LockOfferAsync(db, id, options.Value.OfferLockTimeout, cancellationToken)
            is not { } offers
        )
            return RewardsProblems.OfferBusy(http);
        if (offers.SingleOrDefault() is not { } offer)
            return RewardsProblems.OfferNotFound();
        if (!offer.IsActive)
            return ApiProblems.Problem(
                StatusCodes.Status409Conflict,
                "Offer is not available",
                RewardsErrorCodes.OfferInactive
            );
        if (offer.Version != version)
            return ApiProblems.Problem(
                StatusCodes.Status409Conflict,
                "Offer changed since it was shown",
                RewardsErrorCodes.OfferChanged
            );

        var userId = users.GetUserId(http.User)!;
        var coupon = new Coupon
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            OfferId = offer.Id,
            OfferName = offer.Name,
            PointsCost = offer.PointsCost,
            Kind = offer.Kind,
            DiscountPerLiter = offer.DiscountPerLiter,
            MaxLiters = offer.MaxLiters,
            MinLiters = offer.MinLiters,
            Status = CouponStatus.Purchased,
        };

        var spent = await points.SpendAsync(
            userId,
            offer.PointsCost,
            PointsLedgerEntryType.CouponExchange,
            coupon.Id,
            cancellationToken
        );
        switch (spent.Outcome)
        {
            case SpendOutcome.CustomerBusy:
                return LoyaltyProblems.CustomerBusy(http);
            case SpendOutcome.InsufficientPoints:
                return ApiProblems.Problem(
                    StatusCodes.Status409Conflict,
                    "Not enough points",
                    RewardsErrorCodes.InsufficientPoints
                );
        }

        // Added after a successful debit, so nothing is tracked for the failure paths. A failed
        // attempt leaves no rows anyway: idempotency rolls back to its savepoint on a non-2xx.
        db.Coupons.Add(coupon);
        await db.SaveChangesAsync(cancellationToken);

        return TypedResults.Created(
            $"{MyCouponsEndpoints.Path}/{coupon.Id}",
            new ExchangeResponse(MyCouponsEndpoints.ToResponse(coupon), spent.BalanceAfter)
        );
    }

    /// <returns>null when the wait timed out; the transaction is then aborted and must be rolled back.</returns>
    private static async Task<List<RewardOffer>?> LockOfferAsync(
        AppDbContext db,
        Guid id,
        TimeSpan timeout,
        CancellationToken cancellationToken
    )
    {
        var timeoutMs = (long)timeout.TotalMilliseconds;
        await db.Database.ExecuteSqlRawAsync(
            $"SET LOCAL lock_timeout = {timeoutMs}",
            cancellationToken
        );
        List<RewardOffer> offers;
        try
        {
            offers = await db
                .RewardOffers.FromSql(
                    $"""SELECT * FROM "RewardOffers" WHERE "Id" = {id} FOR SHARE"""
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
            return null;
        }

        await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL lock_timeout TO DEFAULT",
            cancellationToken
        );
        return offers;
    }
}
