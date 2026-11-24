using FuelClub.Api.Modules.Loyalty.Domain;

namespace FuelClub.Api.Modules.Rewards.Domain;

public sealed record RewardOfferInput(
    string? Name,
    string? Description,
    int PointsCost,
    RewardKind Kind,
    decimal? DiscountPerLiter,
    decimal? MaxLiters,
    decimal? MinLiters = null
);

public sealed record RewardsValidationError(string Field, string Code)
    : FuelClub.Api.Infrastructure.IFieldError;

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
        input with
        {
            Name = input.Name?.Trim(),
            Description = input.Description?.Trim() ?? string.Empty,
        };

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

        if (input.Kind == RewardKind.Product)
        {
            // A product has no per-liter terms; a value sent anyway is a form mistake, not ignored.
            if (input.DiscountPerLiter is not null)
                errors.Add(new("discountPerLiter", RewardsErrorCodes.NotForProduct));
            if (input.MaxLiters is not null)
                errors.Add(new("maxLiters", RewardsErrorCodes.NotForProduct));
            if (input.MinLiters is not null)
                errors.Add(new("minLiters", RewardsErrorCodes.NotForProduct));
            return errors;
        }

        if (input.DiscountPerLiter is not { } rate)
            errors.Add(new("discountPerLiter", RewardsErrorCodes.RequiredField));
        else if (rate <= 0m || rate > MaxDiscountPerLiter)
            errors.Add(new("discountPerLiter", RewardsErrorCodes.DiscountPerLiterOutOfRange));
        else if (!FuelPurchaseRules.HasAtMostDecimals(rate, DiscountPerLiterScale))
            errors.Add(new("discountPerLiter", RewardsErrorCodes.DiscountPerLiterPrecision));

        if (input.MaxLiters is not { } liters)
            errors.Add(new("maxLiters", RewardsErrorCodes.RequiredField));
        else if (liters <= 0m || liters > MaxDiscountLiters)
            errors.Add(new("maxLiters", RewardsErrorCodes.MaxLitersOutOfRange));
        else if (!FuelPurchaseRules.HasAtMostDecimals(liters, MaxLitersScale))
            errors.Add(new("maxLiters", RewardsErrorCodes.MaxLitersPrecision));

        // Optional; may be above MaxLiters ("from 30 l, discount on 20 l").
        if (input.MinLiters is { } min)
        {
            if (min <= 0m || min > MaxDiscountLiters)
                errors.Add(new("minLiters", RewardsErrorCodes.MinLitersOutOfRange));
            else if (!FuelPurchaseRules.HasAtMostDecimals(min, MaxLitersScale))
                errors.Add(new("minLiters", RewardsErrorCodes.MinLitersPrecision));
        }

        return errors;
    }
}
