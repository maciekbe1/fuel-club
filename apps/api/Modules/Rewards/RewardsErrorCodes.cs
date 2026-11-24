namespace FuelClub.Api.Modules.Rewards;

public static class RewardsErrorCodes
{
    public const string RequiredField = "rewards.required_field";
    public const string InvalidFormat = "rewards.invalid_format";
    public const string TooLong = "rewards.too_long";
    public const string PointsCostOutOfRange = "rewards.points_cost_out_of_range";
    public const string DiscountPerLiterOutOfRange = "rewards.discount_per_liter_out_of_range";
    public const string DiscountPerLiterPrecision = "rewards.discount_per_liter_precision";
    public const string MaxLitersOutOfRange = "rewards.max_liters_out_of_range";
    public const string MaxLitersPrecision = "rewards.max_liters_precision";
    public const string MinLitersOutOfRange = "rewards.min_liters_out_of_range";
    public const string MinLitersPrecision = "rewards.min_liters_precision";
    public const string MinLitersNotReached = "rewards.min_liters_not_reached";
    public const string VersionOutOfRange = "rewards.version_out_of_range";
    public const string ValidationFailed = "rewards.validation_failed";
    public const string OfferNotFound = "rewards.offer_not_found";
    public const string OfferVersionConflict = "rewards.offer_version_conflict";
    public const string OfferInactive = "rewards.offer_inactive";
    public const string OfferBusy = "rewards.offer_busy";
    public const string OfferChanged = "rewards.offer_changed";
    public const string InsufficientPoints = "rewards.insufficient_points";
    public const string CouponNotFound = "rewards.coupon_not_found";
    public const string CouponRedeemed = "rewards.coupon_redeemed";
    public const string CouponBusy = "rewards.coupon_busy";
    public const string NotForProduct = "rewards.not_for_product";
    public const string KindImmutable = "rewards.kind_immutable";
    public const string CouponWrongKind = "rewards.coupon_wrong_kind";
}
