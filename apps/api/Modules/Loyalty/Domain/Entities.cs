namespace FuelClub.Api.Modules.Loyalty.Domain;

/// <summary>Points balance of one user. Created on the first credit; a missing row means balance 0.</summary>
public sealed class LoyaltyAccount
{
    public string UserId { get; set; } = null!;
    public int Balance { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Maps to PostgreSQL xmin; guards concurrent balance updates.</summary>
    public uint Version { get; set; }
}

public sealed class FuelPurchase
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = null!;
    public string StationCode { get; set; } = null!;
    public string RegisterCode { get; set; } = null!;
    public DateOnly SaleDate { get; set; }
    public string ReceiptNumber { get; set; } = null!;
    /// <summary>Receipt value before any discount (what the dispenser shows).</summary>
    public decimal GrossAmount { get; set; }

    /// <summary>Coupon discount applied to this purchase; 0 without a coupon. Computed by the server.</summary>
    public decimal Discount { get; set; }

    /// <summary>GrossAmount - Discount; points are granted on this amount (0 is valid).</summary>
    public decimal PaidAmount { get; set; }

    public decimal Liters { get; set; }

    /// <summary>Snapshot of the points granted; later rule changes do not alter history.</summary>
    public int PointsAwarded { get; set; }

    public string RegisteredByUserId { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

/// <summary>The sign of Delta depends on the type (enforced in the database).</summary>
public enum PointsLedgerEntryType
{
    /// <summary>Credit (Delta &gt; 0). SourceId = FuelPurchase.Id.</summary>
    FuelPurchase,

    /// <summary>Debit (Delta &lt; 0) for points exchanged for a coupon. SourceId = Coupon.Id.</summary>
    CouponExchange,
}

/// <summary>Append-only history of balance changes. Sum of Delta per user equals the balance.</summary>
public sealed class PointsLedgerEntry
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = null!;
    public int Delta { get; set; }
    public PointsLedgerEntryType Type { get; set; }
    public Guid SourceId { get; set; }
    public int BalanceAfter { get; set; }
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Order of a customer's history. Assigned on insert, i.e. after the customer lock, unlike
    /// CreatedAt (transaction start). Every ledger write must hold <c>CustomerLock</c>. Gaps after
    /// rollbacks are normal.
    /// </summary>
    public long Sequence { get; set; }
}
