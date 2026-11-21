namespace FuelClub.Api.Modules.Loyalty.Domain;

/// <summary>1 point for every full 1 PLN actually paid (after a coupon discount): floor(paid).</summary>
public static class PointsPolicy
{
    /// <summary>
    /// <paramref name="paidAmount"/> may be 0 (a discount covering the whole purchase): that gives
    /// 0 points and is not an error.
    /// </summary>
    public static int ForPaidAmount(decimal paidAmount)
    {
        if (paidAmount < 0m || paidAmount > FuelPurchaseRules.MaxAmount)
            throw new ArgumentOutOfRangeException(
                nameof(paidAmount),
                paidAmount,
                "Paid amount must be between 0 and the maximum purchase amount."
            );

        return (int)decimal.Floor(paidAmount);
    }
}
