using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Assistant.Tracing;

/// <summary>
/// One price of one model from a date (ADR 0018): USD per million input and output tokens. Output
/// tokens already include the provider's thinking tokens (GeminiChatModel adds them), so no other
/// category is priced separately. A new price is a new entry with a new version: costs of earlier
/// turns were stored with their version and do not change.
/// </summary>
public sealed class ModelPrice
{
    public string Version { get; set; } = "";
    public string Provider { get; set; } = "";
    public string Model { get; set; } = "";
    public DateTimeOffset EffectiveFrom { get; set; }
    public decimal InputUsdPerMillion { get; set; }
    public decimal OutputUsdPerMillion { get; set; }
}

public sealed class ModelPricingOptions
{
    public const string Section = "Assistant:Pricing";
    public List<ModelPrice> Prices { get; set; } = [];
}

public sealed class ModelPricingValidator : IValidateOptions<ModelPricingOptions>
{
    public ValidateOptionsResult Validate(string? name, ModelPricingOptions options)
    {
        var errors = new List<string>();
        foreach (var price in options.Prices)
        {
            if (
                string.IsNullOrWhiteSpace(price.Version)
                || price.Version.Length > 60
                || string.IsNullOrWhiteSpace(price.Provider)
                || string.IsNullOrWhiteSpace(price.Model)
            )
                errors.Add(
                    "Assistant:Pricing: every price needs a version (<= 60), provider and model."
                );
            if (price.InputUsdPerMillion < 0 || price.OutputUsdPerMillion < 0)
                errors.Add($"Assistant:Pricing {price.Version}: prices must be >= 0.");
        }
        if (options.Prices.GroupBy(p => p.Version).Any(g => g.Count() > 1))
            errors.Add("Assistant:Pricing: versions must be unique.");
        // Two prices of one model from the same moment: which one applies would depend on order.
        if (
            options
                .Prices.GroupBy(p => (p.Provider, p.Model, p.EffectiveFrom))
                .Any(g => g.Count() > 1)
        )
            errors.Add("Assistant:Pricing: one price per provider, model and EffectiveFrom.");
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

public sealed record TurnCost(decimal? Usd, string? PriceVersion);

public sealed class ModelPricing(IOptions<ModelPricingOptions> options)
{
    /// <summary>
    /// The demo adapter costs nothing to price (null, no version: "not applicable", told apart by
    /// Demo). Unknown usage or no price for the model: null cost - unknown, never zero.
    /// </summary>
    public TurnCost For(AssistantTurn turn)
    {
        if (turn.Demo)
            return new(null, null);
        var price = options
            .Value.Prices.Where(p =>
                p.Provider == turn.Provider
                && p.Model == turn.Model
                && p.EffectiveFrom <= turn.StartedAt
            )
            .MaxBy(p => p.EffectiveFrom);
        // No model call (client gone before it, failure before it): nothing was spent.
        if (turn.ModelSteps == 0)
            return new(0m, price?.Version);
        if (
            price is null
            || turn.InputTokens is not { } input
            || turn.OutputTokens is not { } output
        )
            return new(null, price?.Version);
        var usd =
            (input * price.InputUsdPerMillion + output * price.OutputUsdPerMillion) / 1_000_000m;
        return new(Math.Round(usd, 6), price.Version);
    }
}
