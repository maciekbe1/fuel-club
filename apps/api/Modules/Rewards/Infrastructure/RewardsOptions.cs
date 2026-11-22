using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Rewards.Infrastructure;

public sealed class RewardsOptions
{
    public const string Section = "Rewards";
    public static readonly TimeSpan MinLockTimeout = TimeSpan.FromMilliseconds(1);
    public static readonly TimeSpan MaxLockTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long an exchange waits for a conflicting lock on the offer row (an admin edit).</summary>
    public TimeSpan OfferLockTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>lock_timeout = 0 means "wait forever" in PostgreSQL, so the bounds are enforced at startup.</summary>
public sealed class RewardsOptionsValidator : IValidateOptions<RewardsOptions>
{
    public ValidateOptionsResult Validate(string? name, RewardsOptions options) =>
        options.OfferLockTimeout >= RewardsOptions.MinLockTimeout && options.OfferLockTimeout <= RewardsOptions.MaxLockTimeout
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{RewardsOptions.Section}:OfferLockTimeout must be between {RewardsOptions.MinLockTimeout} and {RewardsOptions.MaxLockTimeout}; got {options.OfferLockTimeout}.");
}
