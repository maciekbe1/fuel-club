using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Http.HttpResults;

namespace FuelClub.Api.Modules.Rewards;

/// <summary>Error responses shared by Rewards endpoints.</summary>
internal static class RewardsProblems
{
    public static ProblemHttpResult Validation(string title, IEnumerable<RewardsValidationError> errors) =>
        ApiProblems.Validation(title, RewardsErrorCodes.ValidationFailed, errors);

    public static ProblemHttpResult OfferNotFound() =>
        ApiProblems.Problem(StatusCodes.Status404NotFound, "Offer not found", RewardsErrorCodes.OfferNotFound);
}
