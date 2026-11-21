namespace FuelClub.Api.Modules.Rewards;

public static class RewardsErrorCodes
{
    public const string RequiredField = "rewards.required_field";
    public const string TooLong = "rewards.too_long";
    public const string PointsCostOutOfRange = "rewards.points_cost_out_of_range";
    public const string DiscountPerLiterOutOfRange = "rewards.discount_per_liter_out_of_range";
    public const string DiscountPerLiterPrecision = "rewards.discount_per_liter_precision";
    public const string MaxLitersOutOfRange = "rewards.max_liters_out_of_range";
    public const string MaxLitersPrecision = "rewards.max_liters_precision";
    public const string VersionOutOfRange = "rewards.version_out_of_range";
    public const string ValidationFailed = "rewards.validation_failed";
    public const string OfferNotFound = "rewards.offer_not_found";
    public const string OfferVersionConflict = "rewards.offer_version_conflict";
}
