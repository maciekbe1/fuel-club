using FuelClub.Api.Modules.Loyalty.Domain;

namespace FuelClub.Api.Modules.Rewards.Domain;

public sealed record RewardOfferInput(string? Name, string? Description, int PointsCost, decimal DiscountPerLiter, decimal MaxLiters);

public sealed record RewardsValidationError(string Field, string Code);

/// <summary>
/// Offer rules. Precision is checked here: PostgreSQL numeric(p,s) rounds extra decimals on write
/// instead of rejecting them, so the database cannot catch 0.205 zł/l.
/// </summary>
public static class RewardOfferRules
{
    public const int NameMaxLength = 100;
    public const int DescriptionMaxLength = 500;
    public const int MaxPointsCost = 1_000_000;

    public const decimal MaxDiscountPerLiter = 10m;
    public const int DiscountPerLiterPrecision = 4;
    public const int DiscountPerLiterScale = 2;

    public const decimal MaxDiscountLiters = 1000m;
    public const int MaxLitersPrecision = 7;
    public const int MaxLitersScale = FuelPurchaseRules.LitersScale;

    public static RewardOfferInput Normalize(RewardOfferInput input) =>
        input with { Name = input.Name?.Trim(), Description = input.Description?.Trim() ?? string.Empty };

    public static IReadOnlyList<RewardsValidationError> Validate(RewardOfferInput input)
    {
        var errors = new List<RewardsValidationError>();

        if (string.IsNullOrEmpty(input.Name))
            errors.Add(new("name", RewardsErrorCodes.RequiredField));
        else if (input.Name.Length > NameMaxLength)
            errors.Add(new("name", RewardsErrorCodes.TooLong));

        if ((input.Description ?? string.Empty).Length > DescriptionMaxLength)
            errors.Add(new("description", RewardsErrorCodes.TooLong));

        if (input.PointsCost is < 1 or > MaxPointsCost)
            errors.Add(new("pointsCost", RewardsErrorCodes.PointsCostOutOfRange));

        if (input.DiscountPerLiter <= 0m || input.DiscountPerLiter > MaxDiscountPerLiter)
            errors.Add(new("discountPerLiter", RewardsErrorCodes.DiscountPerLiterOutOfRange));
        else if (!FuelPurchaseRules.HasAtMostDecimals(input.DiscountPerLiter, DiscountPerLiterScale))
            errors.Add(new("discountPerLiter", RewardsErrorCodes.DiscountPerLiterPrecision));

        if (input.MaxLiters <= 0m || input.MaxLiters > MaxDiscountLiters)
            errors.Add(new("maxLiters", RewardsErrorCodes.MaxLitersOutOfRange));
        else if (!FuelPurchaseRules.HasAtMostDecimals(input.MaxLiters, MaxLitersScale))
            errors.Add(new("maxLiters", RewardsErrorCodes.MaxLitersPrecision));

        return errors;
    }
}
