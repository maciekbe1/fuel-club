using FuelClub.Api.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Rewards.Catalog;

public sealed record RewardResponse(
    Guid Id,
    string Name,
    string Description,
    int PointsCost,
    decimal DiscountPerLiter,
    decimal MaxLiters);

public sealed record RewardsResponse(IReadOnlyList<RewardResponse> Items);

/// <summary>
/// Active offers for customers. No paging (the catalog is small); the order is fully determined,
/// also for equal cost and name.
/// </summary>
public static class CatalogEndpoint
{
    public static async Task<Ok<RewardsResponse>> Handle(AppDbContext db, CancellationToken cancellationToken)
    {
        var items = await db.RewardOffers
            .AsNoTracking()
            .Where(offer => offer.IsActive)
            .OrderBy(offer => offer.PointsCost)
            .ThenBy(offer => offer.Name)
            .ThenBy(offer => offer.Id)
            .Select(offer => new RewardResponse(
                offer.Id, offer.Name, offer.Description, offer.PointsCost, offer.DiscountPerLiter, offer.MaxLiters))
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new RewardsResponse(items));
    }
}
