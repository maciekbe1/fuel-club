using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Loyalty.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FuelClub.Api.Modules.Loyalty;

/// <summary>Error responses shared by Loyalty endpoints.</summary>
internal static class LoyaltyProblems
{
    public static ProblemHttpResult Validation(
        string title,
        IEnumerable<LoyaltyValidationError> errors
    ) => ApiProblems.Validation(title, LoyaltyErrorCodes.ValidationFailed, errors);

    /// <summary>Another operation holds the customer lock; transient, the client retries with the same key.</summary>
    public static ProblemHttpResult CustomerBusy(HttpContext http) =>
        ApiProblems.Busy(
            http,
            "Another operation on this customer is in progress",
            LoyaltyErrorCodes.CustomerBusy
        );

    public static ProblemHttpResult CardMissing() =>
        ApiProblems.Problem(
            StatusCodes.Status404NotFound,
            "Loyalty card not issued",
            LoyaltyErrorCodes.CardMissing
        );

    public static ProblemHttpResult CardNotFound() =>
        ApiProblems.Problem(
            StatusCodes.Status404NotFound,
            "Loyalty card not found",
            LoyaltyErrorCodes.CardNotFound
        );
}
