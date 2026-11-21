namespace FuelClub.Api.Modules.Rewards.Domain;

public readonly record struct DiscountResult(decimal Discount, decimal PaidAmount);

/// <summary>
/// discount = round(perLiter * min(liters, maxLiters), 2, AwayFromZero), at most the gross amount.
/// Always computed by the server from the coupon snapshot; a client never sends a discount.
/// </summary>
public static class CouponDiscount
{
    public static DiscountResult Calculate(decimal discountPerLiter, decimal maxLiters, decimal liters, decimal grossAmount)
    {
        if (discountPerLiter <= 0m || maxLiters <= 0m)
            throw new ArgumentOutOfRangeException(nameof(discountPerLiter), "Coupon benefit must be positive.");
        if (liters <= 0m || grossAmount <= 0m)
            throw new ArgumentOutOfRangeException(nameof(liters), "Purchase must be validated first.");

        var raw = discountPerLiter * Math.Min(liters, maxLiters);
        var discount = Math.Min(Math.Round(raw, 2, MidpointRounding.AwayFromZero), grossAmount);
        return new DiscountResult(discount, grossAmount - discount);
    }
}
