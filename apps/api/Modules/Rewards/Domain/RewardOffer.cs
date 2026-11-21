namespace FuelClub.Api.Modules.Rewards.Domain;

/// <summary>
/// Offer in the catalog: a per-liter fuel discount bought with points. Never deleted, only
/// deactivated: issued coupons keep pointing at it and keep working.
/// </summary>
public sealed class RewardOffer
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
    public string Description { get; set; } = null!;
    public int PointsCost { get; set; }

    /// <summary>PLN per liter, e.g. 0.20.</summary>
    public decimal DiscountPerLiter { get; set; }

    /// <summary>Liters of one purchase covered by the discount.</summary>
    public decimal MaxLiters { get; set; }

    /// <summary>Inactive: no new exchanges. Coupons already issued stay valid.</summary>
    public bool IsActive { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>Maps to PostgreSQL xmin; guards concurrent edits.</summary>
    public uint Version { get; set; }
}
