using FuelClub.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Rewards.Catalog;

public sealed record RewardResponse(
    Guid Id,
    string Name,
    string Description,
    int PointsCost,
    string Kind,
    decimal? DiscountPerLiter,
    decimal? MaxLiters,
    decimal? MinLiters,
    int Version
);

public sealed record RewardsResponse(IReadOnlyList<RewardResponse> Items);

/// <summary>
/// Active offers for customers. <c>Version</c> goes back in the exchange request, so the customer
/// is charged only for the terms they saw. No paging (the catalog is small); the order is fully determined,
/// also for equal cost and name.
/// </summary>
public static class CatalogEndpoint
{
    public static async Task<Ok<RewardsResponse>> Handle(
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var items = await ActiveOffers(db).ToListAsync(cancellationToken);
        return TypedResults.Ok(new RewardsResponse(items));
    }

    /// <summary>The customer catalog; also read by the assistant's list_available_rewards.</summary>
    public static IQueryable<RewardResponse> ActiveOffers(AppDbContext db) =>
        db
            .RewardOffers.AsNoTracking()
            .Where(offer => offer.IsActive)
            .OrderBy(offer => offer.PointsCost)
            .ThenBy(offer => offer.Name)
            .ThenBy(offer => offer.Id)
            .Select(offer => new RewardResponse(
                offer.Id,
                offer.Name,
                offer.Description,
                offer.PointsCost,
                offer.Kind.ToString(),
                offer.DiscountPerLiter,
                offer.MaxLiters,
                offer.MinLiters,
                offer.Version
            ));
}
