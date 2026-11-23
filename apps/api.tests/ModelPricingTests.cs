using FuelClub.Api.Modules.Assistant.Tracing;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Tests;

public sealed class ModelPricingTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 23, 12, 0, 0, TimeSpan.Zero);

    private static ModelPricing Pricing(params ModelPrice[] prices) =>
        new(Options.Create(new ModelPricingOptions { Prices = [.. prices] }));

    private static ModelPrice Price(
        string version,
        DateTimeOffset from,
        decimal input,
        decimal output
    ) =>
        new()
        {
            Version = version,
            Provider = "gemini",
            Model = "g",
            EffectiveFrom = from,
            InputUsdPerMillion = input,
            OutputUsdPerMillion = output,
        };

    private static AssistantTurn Turn(bool demo = false, int? input = 1000, int? output = 200) =>
        new()
        {
            CorrelationId = "c",
            UserPseudonym = "p",
            Provider = demo ? "demo" : "gemini",
            Model = demo ? "demo-rules" : "g",
            PromptVersion = "v",
            Demo = demo,
            StartedAt = Now,
            InputTokens = input,
            OutputTokens = output,
        };

    [Fact]
    public void Cost_is_usage_times_the_price_in_force_at_the_turn()
    {
        var pricing = Pricing(
            Price("old", Now.AddDays(-30), 1m, 10m),
            Price("new", Now.AddDays(-1), 0.30m, 2.50m),
            Price("future", Now.AddDays(1), 100m, 100m)
        );

        // 1000 x 0.30 / 1M + 200 x 2.50 / 1M
        Assert.Equal(new TurnCost(0.0008m, "new"), pricing.For(Turn()));
    }

    [Fact]
    public void Demo_is_not_applicable_and_missing_usage_or_price_is_unknown_not_zero()
    {
        var pricing = Pricing(Price("p", Now.AddDays(-1), 0.30m, 2.50m));

        Assert.Equal(new TurnCost(null, null), pricing.For(Turn(demo: true)));
        Assert.Equal(new TurnCost(null, "p"), pricing.For(Turn(input: null, output: null)));
        Assert.Equal(new TurnCost(null, null), Pricing().For(Turn()));
    }

    [Fact]
    public void Prices_are_validated()
    {
        var validator = new ModelPricingValidator();
        var ok = new ModelPricingOptions { Prices = [Price("a", Now, 1m, 1m)] };
        var duplicate = new ModelPricingOptions
        {
            Prices = [Price("a", Now, 1m, 1m), Price("a", Now, 2m, 2m)],
        };
        var negative = new ModelPricingOptions { Prices = [Price("b", Now, -1m, 1m)] };

        Assert.True(validator.Validate(null, ok).Succeeded);
        Assert.False(validator.Validate(null, duplicate).Succeeded);
        Assert.False(validator.Validate(null, negative).Succeeded);
    }
}
