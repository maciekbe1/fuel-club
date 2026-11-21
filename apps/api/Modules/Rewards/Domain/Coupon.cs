namespace FuelClub.Api.Modules.Rewards.Domain;

/// <summary>Purchased -> Active -> Redeemed. Points are paid once, at the exchange.</summary>
public enum CouponStatus
{
    /// <summary>Bought with points; no code yet, so there is nothing to scan.</summary>
    Purchased,

    /// <summary>Activated by the owner; has a redemption code.</summary>
    Active,

    /// <summary>Used on exactly one fuel purchase.</summary>
    Redeemed,
}

/// <summary>
/// A customer's coupon. Name, cost and benefit are a snapshot taken at the exchange: later edits
/// or deactivation of the offer do not change it. State consistency is enforced by CHECK constraints.
/// </summary>
public sealed class Coupon
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = null!;
    public Guid OfferId { get; set; }

    public string OfferName { get; set; } = null!;
    public int PointsCost { get; set; }
    public decimal DiscountPerLiter { get; set; }
    public decimal MaxLiters { get; set; }

    public CouponStatus Status { get; set; }

    /// <summary>Redemption code, generated at activation. Never logged.</summary>
    public string? Code { get; set; }

    public DateTimeOffset PurchasedAt { get; set; }
    public DateTimeOffset? ActivatedAt { get; set; }
    public DateTimeOffset? RedeemedAt { get; set; }

    /// <summary>The fuel purchase the discount was applied to; unique, so one coupon per purchase.</summary>
    public Guid? RedeemedPurchaseId { get; set; }
}
