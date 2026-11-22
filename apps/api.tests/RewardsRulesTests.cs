using System.Globalization;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Rewards;
using FuelClub.Api.Modules.Rewards.Domain;

namespace FuelClub.Api.Tests;

public sealed class CouponDiscountTests
{
    private static decimal D(string value) => decimal.Parse(value, CultureInfo.InvariantCulture);

    [Theory]
    // rate, max liters, liters, gross -> discount, paid
    [InlineData("0.20", "50", "40.5", "250.00", "8.10", "241.90")]
    [InlineData("0.20", "50", "50", "300.00", "10.00", "290.00")]
    [InlineData("0.20", "50", "60.123", "400.00", "10.00", "390.00")] // limit applies
    [InlineData("0.15", "50", "12.345", "80.00", "1.85", "78.15")] // 1.85175
    [InlineData("0.15", "50", "0.3", "2.00", "0.05", "1.95")] // 0.045: away from zero, banker's would give 0.04
    [InlineData("0.05", "50", "0.1", "1.00", "0.01", "0.99")] // 0.005 -> 0.01
    [InlineData("0.25", "50", "10.01", "60.00", "2.50", "57.50")] // 2.5025
    [InlineData("10.00", "100", "5", "30.00", "30.00", "0.00")] // capped at the gross amount
    [InlineData("0.20", "50", "0.5", "0.08", "0.08", "0.00")]
    public void Computes_discount_and_paid_amount(
        string rate,
        string maxLiters,
        string liters,
        string gross,
        string discount,
        string paid
    )
    {
        var result = CouponDiscount.Calculate(D(rate), D(maxLiters), D(liters), D(gross));

        Assert.Equal(D(discount), result.Discount);
        Assert.Equal(D(paid), result.PaidAmount);
        Assert.Equal(D(gross), result.Discount + result.PaidAmount);
    }

    [Fact]
    public void Paid_amount_zero_gives_zero_points()
    {
        var result = CouponDiscount.Calculate(10m, 100m, 5m, 30m);
        Assert.Equal(0, PointsPolicy.ForPaidAmount(result.PaidAmount));
    }

    [Fact]
    public void Points_come_from_the_paid_amount() =>
        Assert.Equal(
            241,
            PointsPolicy.ForPaidAmount(CouponDiscount.Calculate(0.20m, 50m, 40.5m, 250m).PaidAmount)
        );

    [Theory]
    [InlineData("0", "50", "10", "100")]
    [InlineData("0.20", "0", "10", "100")]
    [InlineData("0.20", "50", "0", "100")]
    [InlineData("0.20", "50", "10", "0")]
    public void Rejects_unvalidated_input(
        string rate,
        string maxLiters,
        string liters,
        string gross
    ) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            CouponDiscount.Calculate(D(rate), D(maxLiters), D(liters), D(gross))
        );
}

public sealed class CouponCodeTests
{
    [Fact]
    public void Generated_codes_have_the_prefix_and_are_valid_and_distinct()
    {
        var codes = Enumerable.Range(0, 1000).Select(_ => CouponCode.Generate()).ToList();

        Assert.All(
            codes,
            code =>
            {
                Assert.Equal(27, code.Length);
                Assert.StartsWith("K", code);
                Assert.True(CouponCode.IsValid(code), code);
                // The random part is a full card-style encoding, not shortened by the prefix.
                Assert.True(LoyaltyCardCode.IsValid(code[1..]), code);
            }
        );
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Card_and_coupon_codes_are_never_confused()
    {
        var card = LoyaltyCardCode.Generate();
        var coupon = CouponCode.Generate();

        Assert.False(CouponCode.IsValid(card));
        Assert.False(LoyaltyCardCode.IsValid(coupon));
        Assert.False(CouponCode.IsValid("K" + card[..25]));
        Assert.False(CouponCode.IsValid("K8" + new string('0', 25))); // first random char is 0-7
        Assert.False(CouponCode.IsValid("X" + card));
    }

    [Theory]
    [InlineData("k-7zzz-zzzz-zzzz-zzzz-zzzz-zzzz-zz", "K7ZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    [InlineData(" K0000 0000 0000 0000 0000 0000 0I ", "K00000000000000000000000001")]
    public void Normalize_accepts_typed_variants(string input, string expected)
    {
        Assert.Equal(expected, CouponCode.Normalize(input));
        Assert.True(CouponCode.IsValid(CouponCode.Normalize(input)));
    }
}

public sealed class RewardOfferRulesTests
{
    private static IReadOnlyList<RewardsValidationError> Validate(
        string? name = "Rabat 20 gr/l",
        string? description = "Na dowolne paliwo",
        int cost = 500,
        decimal rate = 0.20m,
        decimal maxLiters = 50m
    ) =>
        RewardOfferRules.Validate(
            RewardOfferRules.Normalize(
                new RewardOfferInput(name, description, cost, rate, maxLiters)
            )
        );

    private static void AssertSingle(
        IReadOnlyList<RewardsValidationError> errors,
        string field,
        string code
    ) => Assert.Equal([new RewardsValidationError(field, code)], errors);

    [Fact]
    public void Accepts_a_valid_offer_and_the_limits()
    {
        Assert.Empty(Validate());
        Assert.Empty(Validate(cost: 1, rate: 0.01m, maxLiters: 0.001m));
        Assert.Empty(
            Validate(
                cost: RewardOfferRules.MaxPointsCost,
                rate: 10m,
                maxLiters: 1000m,
                description: null
            )
        );
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Requires_a_name(string? name) =>
        AssertSingle(Validate(name: name), "name", RewardsErrorCodes.RequiredField);

    [Fact]
    public void Limits_name_and_description_length()
    {
        AssertSingle(Validate(name: new string('a', 101)), "name", RewardsErrorCodes.TooLong);
        AssertSingle(
            Validate(description: new string('a', 501)),
            "description",
            RewardsErrorCodes.TooLong
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(1_000_001)]
    public void Rejects_points_cost_outside_range(int cost) =>
        AssertSingle(Validate(cost: cost), "pointsCost", RewardsErrorCodes.PointsCostOutOfRange);

    [Theory]
    [InlineData("0")]
    [InlineData("-0.20")]
    [InlineData("10.01")]
    public void Rejects_rate_outside_range(string rate) =>
        AssertSingle(
            Validate(rate: decimal.Parse(rate, CultureInfo.InvariantCulture)),
            "discountPerLiter",
            RewardsErrorCodes.DiscountPerLiterOutOfRange
        );

    /// <summary>numeric(4,2) would silently store 0.205 as 0.21, so the application must reject it.</summary>
    [Fact]
    public void Rejects_rate_with_more_than_two_decimals() =>
        AssertSingle(
            Validate(rate: 0.205m),
            "discountPerLiter",
            RewardsErrorCodes.DiscountPerLiterPrecision
        );

    [Theory]
    [InlineData("0")]
    [InlineData("1000.001")]
    public void Rejects_max_liters_outside_range(string liters) =>
        AssertSingle(
            Validate(maxLiters: decimal.Parse(liters, CultureInfo.InvariantCulture)),
            "maxLiters",
            RewardsErrorCodes.MaxLitersOutOfRange
        );

    [Fact]
    public void Rejects_max_liters_with_more_than_three_decimals() =>
        AssertSingle(
            Validate(maxLiters: 50.0005m),
            "maxLiters",
            RewardsErrorCodes.MaxLitersPrecision
        );
}
