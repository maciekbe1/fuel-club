namespace FuelClub.Api.Modules.Rewards.Domain;

/// <summary>What a coupon gives: a fuel discount used with a purchase, or a product handed over at the till.</summary>
public enum RewardKind
{
    FuelDiscount,
    Product,
}

/// <summary>
/// Offer in the catalog bought with points: a per-liter fuel discount or a product (ADR 0013). Never deleted, only
/// deactivated: issued coupons keep pointing at it and keep working.
/// </summary>
public sealed class RewardOffer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public int PointsCost { get; set; }

    /// <summary>Set at creation, never changed: issued coupons carry it in their snapshot.</summary>
    public RewardKind Kind { get; set; }

    /// <summary>PLN per liter, e.g. 0.20. Only for a fuel discount.</summary>
    public decimal? DiscountPerLiter { get; set; }

    /// <summary>Liters of one purchase covered by the discount. Only for a fuel discount.</summary>
    public decimal? MaxLiters { get; set; }

    /// <summary>Inactive: no new exchanges. Coupons already issued stay valid.</summary>
    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>
    /// Edit version, starts at 1. An edit sends the version it was based on; the conditional UPDATE
    /// increments it atomically and rejects a stale form (409).
    /// </summary>
    public int Version { get; set; } = 1;
}
