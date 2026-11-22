using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FuelClub.Api.Modules.Rewards;

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

    public static ProblemHttpResult OfferBusy(HttpContext http) =>
        ApiProblems.Busy(http, "The offer is being changed", RewardsErrorCodes.OfferBusy);
}
