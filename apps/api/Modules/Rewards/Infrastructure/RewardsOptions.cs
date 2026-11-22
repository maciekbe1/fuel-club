using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Rewards.Infrastructure;

public sealed class RewardsOptions
{
    public const string Section = "Rewards";
    public static readonly TimeSpan MinLockTimeout = TimeSpan.FromMilliseconds(1);
    public static readonly TimeSpan MaxLockTimeout = TimeSpan.FromSeconds(30);
    public const int MaxPreviewPermitLimit = 1000;
    public static readonly TimeSpan MinPreviewWindow = TimeSpan.FromSeconds(1);
    public static readonly TimeSpan MaxPreviewWindow = TimeSpan.FromHours(1);

    /// <summary>How long an exchange waits for a conflicting lock on the offer row (an admin edit).</summary>
    public TimeSpan OfferLockTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>How long a purchase waits for a lock on the coupon row held by another transaction.</summary>
    public TimeSpan CouponLockTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>Coupon previews one cashier may make per <see cref="PreviewWindow"/>.</summary>
    public int PreviewPermitLimit { get; set; } = 30;

    public TimeSpan PreviewWindow { get; set; } = TimeSpan.FromMinutes(1);
}

/// <summary>lock_timeout = 0 means "wait forever" in PostgreSQL, so the bounds are enforced at startup.</summary>
public sealed class RewardsOptionsValidator : IValidateOptions<RewardsOptions>
{
    public ValidateOptionsResult Validate(string? name, RewardsOptions options)
    {
        var failures = new List<string>();
        if (
            options.OfferLockTimeout < RewardsOptions.MinLockTimeout
            || options.OfferLockTimeout > RewardsOptions.MaxLockTimeout
        )
            failures.Add(
                $"{RewardsOptions.Section}:OfferLockTimeout must be between {RewardsOptions.MinLockTimeout} and {RewardsOptions.MaxLockTimeout}; got {options.OfferLockTimeout}."
            );
        if (
            options.CouponLockTimeout < RewardsOptions.MinLockTimeout
            || options.CouponLockTimeout > RewardsOptions.MaxLockTimeout
        )
            failures.Add(
                $"{RewardsOptions.Section}:CouponLockTimeout must be between {RewardsOptions.MinLockTimeout} and {RewardsOptions.MaxLockTimeout}; got {options.CouponLockTimeout}."
            );
        if (
            options.PreviewPermitLimit < 1
            || options.PreviewPermitLimit > RewardsOptions.MaxPreviewPermitLimit
        )
            failures.Add(
                $"{RewardsOptions.Section}:PreviewPermitLimit must be between 1 and {RewardsOptions.MaxPreviewPermitLimit}; got {options.PreviewPermitLimit}."
            );
        if (
            options.PreviewWindow < RewardsOptions.MinPreviewWindow
            || options.PreviewWindow > RewardsOptions.MaxPreviewWindow
        )
            failures.Add(
                $"{RewardsOptions.Section}:PreviewWindow must be between {RewardsOptions.MinPreviewWindow} and {RewardsOptions.MaxPreviewWindow}; got {options.PreviewWindow}."
            );
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}
