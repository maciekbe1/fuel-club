using FuelClub.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Loyalty.Points;

/// <summary>The amounts behind the points of one purchase: no date, receipt, station or id.</summary>
public sealed record PurchasePointsBreakdown(
    decimal GrossAmount,
    decimal Discount,
    decimal PaidAmount,
    int PointsAwarded
);

/// <summary>Read by the assistant's explain_purchase_points; always the session user's own.</summary>
public static class PurchasePoints
{
    /// <summary>
    /// The latest purchase by sale date; a tie goes to the one registered later, then to the
    /// larger id, so the answer is the same on every read.
    /// </summary>
    public static Task<PurchasePointsBreakdown?> LatestAsync(
        AppDbContext db,
        string userId,
        CancellationToken cancellationToken
    ) =>
        db
            .FuelPurchases.AsNoTracking()
            .Where(purchase => purchase.UserId == userId)
            .OrderByDescending(purchase => purchase.SaleDate)
            .ThenByDescending(purchase => purchase.CreatedAt)
            .ThenByDescending(purchase => purchase.Id)
            .Select(purchase => new PurchasePointsBreakdown(
                purchase.GrossAmount,
                purchase.Discount,
                purchase.PaidAmount,
                purchase.PointsAwarded
            ))
            .FirstOrDefaultAsync(cancellationToken);
}
