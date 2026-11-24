using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FuelClub.Api.Modules.Rewards;

/// <summary>
/// The 409 of the cashier's coupon endpoints in OpenAPI: a problem with a code, plus, for a
/// handed-over product reward (rewards.coupon_redeemed), when and where it was handed over; for
/// rewards.min_liters_not_reached, the coupon's minLiters.
/// Documentation only - the responses are ProblemDetails with these extensions.
/// </summary>
public sealed record RewardsConflictProblem(
    string Code,
    string? Type = null,
    string? Title = null,
    int? Status = null,
    DateTimeOffset? HandedOverAt = null,
    string? StationCode = null,
    string? RegisterCode = null,
    decimal? MinLiters = null
);

/// <summary>Error responses shared by Rewards endpoints.</summary>
internal static class RewardsProblems
{
    public static ProblemHttpResult Validation(
        string title,
        IEnumerable<RewardsValidationError> errors
    ) => ApiProblems.Validation(title, RewardsErrorCodes.ValidationFailed, errors);

    public static ProblemHttpResult OfferNotFound() =>
        ApiProblems.Problem(
            StatusCodes.Status404NotFound,
            "Offer not found",
            RewardsErrorCodes.OfferNotFound
        );

    /// <summary>Also for another customer's coupon: its existence is not revealed.</summary>
    public static ProblemHttpResult CouponNotFound() =>
        ApiProblems.Problem(
            StatusCodes.Status404NotFound,
            "Coupon not found",
            RewardsErrorCodes.CouponNotFound
        );

    public static ProblemHttpResult CouponRedeemed() =>
        ApiProblems.Problem(
            StatusCodes.Status409Conflict,
            "Coupon was already redeemed",
            RewardsErrorCodes.CouponRedeemed
        );

    /// <summary>
    /// A handed-over product reward, for the owner's card only: when and where (station and till),
    /// so the till can tell a lost answer of its own attempt from an earlier handover without
    /// remembering anything. Shape in OpenAPI: <see cref="RewardsConflictProblem"/>.
    /// </summary>
    public static ProblemHttpResult RewardHandedOver(RewardHandover? handover) =>
        handover is null
            ? CouponRedeemed()
            : TypedResults.Problem(
                statusCode: StatusCodes.Status409Conflict,
                title: "Coupon was already redeemed",
                extensions: new Dictionary<string, object?>
                {
                    ["code"] = RewardsErrorCodes.CouponRedeemed,
                    ["handedOverAt"] = handover.HandedOverAt,
                    ["stationCode"] = handover.StationCode,
                    ["registerCode"] = handover.RegisterCode,
                }
            );

    /// <summary>
    /// The purchase has fewer liters than the coupon's threshold. The coupon stays active: the
    /// customer can fill up more or keep it for later.
    /// </summary>
    public static ProblemHttpResult MinLitersNotReached(decimal minLiters) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "The purchase is below the coupon's minimum liters",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = RewardsErrorCodes.MinLitersNotReached,
                ["minLiters"] = minLiters,
            }
        );

    /// <summary>
    /// A product at the fuel purchase or a fuel discount at the handover. Checked only after the
    /// owner, so it never reveals someone else's coupon.
    /// </summary>
    public static ProblemHttpResult CouponWrongKind() =>
        ApiProblems.Problem(
            StatusCodes.Status409Conflict,
            "This coupon is redeemed differently",
            RewardsErrorCodes.CouponWrongKind
        );

    /// <summary>The coupon row is locked (by another transaction); transient, retried with the same key.</summary>
    public static ProblemHttpResult CouponBusy(HttpContext http) =>
        ApiProblems.Busy(http, "The coupon is being changed", RewardsErrorCodes.CouponBusy);

    public static ProblemHttpResult OfferBusy(HttpContext http) =>
        ApiProblems.Busy(http, "The offer is being changed", RewardsErrorCodes.OfferBusy);
}
