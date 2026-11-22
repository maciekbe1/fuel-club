using FuelClub.Api.Modules.Loyalty;
using FuelClub.Api.Modules.Loyalty.Domain;

namespace FuelClub.Api.Tests;

public sealed class PointsPolicyTests
{
    [Theory]
    [InlineData("0", 0)]
    [InlineData("0.01", 0)]
    [InlineData("0.99", 0)]
    [InlineData("1.00", 1)]
    [InlineData("1.01", 1)]
    [InlineData("99.99", 99)]
    [InlineData("100", 100)]
    [InlineData("123.99", 123)]
    [InlineData("4999.99", 4999)]
    [InlineData("5000", 5000)]
    public void Grants_one_point_per_full_zloty_paid(string amount, int expected) =>
        Assert.Equal(
            expected,
            PointsPolicy.ForPaidAmount(
                decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)
            )
        );

    [Theory]
    [InlineData("-0.01")]
    [InlineData("-1")]
    [InlineData("5000.01")]
    public void Rejects_amounts_outside_the_business_range(string amount) =>
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            PointsPolicy.ForPaidAmount(
                decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)
            )
        );
}

public sealed class FuelPurchaseRulesTests
{
    private static readonly ReceiptKey Receipt = new(
        "ST-001",
        "K1",
        new DateOnly(2026, 11, 21),
        "000123"
    );

    private static IReadOnlyList<LoyaltyValidationError> Validate(
        decimal amount,
        decimal liters,
        ReceiptKey? receipt = null
    ) =>
        FuelPurchaseRules.Validate(
            new FuelPurchaseInput(amount, liters, receipt ?? Receipt),
            DateOnly.MaxValue
        );

    [Fact]
    public void Accepts_a_valid_purchase() => Assert.Empty(Validate(123.99m, 18.375m));

    [Fact]
    public void Accepts_the_upper_limits() => Assert.Empty(Validate(5000m, 0.001m));

    [Theory]
    [InlineData("0")]
    [InlineData("-0.01")]
    [InlineData("5000.01")]
    public void Rejects_amount_outside_range(string amount)
    {
        var errors = Validate(
            decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture),
            10m
        );
        Assert.Contains(
            errors,
            error => error is { Field: "amount", Code: LoyaltyErrorCodes.AmountOutOfRange }
        );
    }

    [Fact]
    public void Rejects_amount_with_more_than_two_decimals() =>
        Assert.Contains(
            Validate(10.005m, 10m),
            error => error is { Field: "amount", Code: LoyaltyErrorCodes.AmountPrecision }
        );

    [Fact]
    public void Trailing_zeros_do_not_count_as_extra_decimals() =>
        Assert.Empty(Validate(10.500m, 10.5000m));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Rejects_non_positive_liters(string liters)
    {
        var errors = Validate(
            10m,
            decimal.Parse(liters, System.Globalization.CultureInfo.InvariantCulture)
        );
        Assert.Contains(
            errors,
            error => error is { Field: "liters", Code: LoyaltyErrorCodes.LitersOutOfRange }
        );
    }

    [Fact]
    public void Rejects_liters_with_more_than_three_decimals() =>
        Assert.Contains(
            Validate(10m, 1.0005m),
            error => error is { Field: "liters", Code: LoyaltyErrorCodes.LitersPrecision }
        );

    [Fact]
    public void Accepts_liters_at_the_column_limit() =>
        Assert.Empty(Validate(10m, FuelPurchaseRules.MaxLiters));

    [Theory]
    [InlineData("10000000")]
    [InlineData("9999999.9991")]
    public void Rejects_liters_that_do_not_fit_the_column(string liters)
    {
        var errors = Validate(
            10m,
            decimal.Parse(liters, System.Globalization.CultureInfo.InvariantCulture)
        );
        Assert.Contains(
            errors,
            error => error is { Field: "liters", Code: LoyaltyErrorCodes.LitersOutOfRange }
        );
    }

    [Fact]
    public void Rejects_default_sale_date() =>
        Assert.Contains(
            Validate(10m, 5m, Receipt with { SaleDate = default }),
            error => error is { Field: "saleDate", Code: LoyaltyErrorCodes.RequiredField }
        );

    [Fact]
    public void Normalize_trims_and_upper_cases_receipt_parts()
    {
        var input = FuelPurchaseRules.Normalize(
            new FuelPurchaseInput(
                10m,
                5m,
                new ReceiptKey("  st-001 ", "\tk1", new DateOnly(2026, 11, 21), " 12/ab ")
            )
        );

        Assert.Equal(
            new ReceiptKey("ST-001", "K1", new DateOnly(2026, 11, 21), "12/AB"),
            input.Receipt
        );
        Assert.Empty(FuelPurchaseRules.Validate(input, DateOnly.MaxValue));
    }

    [Theory]
    [InlineData("st-001", "K1", "A1", "stationCode")]
    [InlineData(" ST-001", "K1", "A1", "stationCode")]
    [InlineData("ST-001", "k1", "A1", "registerCode")]
    [InlineData("ST-001", "K1", "a1", "receiptNumber")]
    [InlineData("ST_001", "K1", "A1", "stationCode")]
    [InlineData("ST-001", "K/1", "A1", "registerCode")]
    [InlineData("ST-001", "K1", "A 1", "receiptNumber")]
    [InlineData("ST-001", "K1", "\u0410\u0412", "receiptNumber")]
    [InlineData("ST-001\n", "K1", "A1", "stationCode")]
    [InlineData("ST-001", "K1\n", "A1", "registerCode")]
    [InlineData("ST-001", "K1", "A1\n", "receiptNumber")]
    [InlineData("ST-001", "K1", "A1\r\n", "receiptNumber")]
    public void Validate_rejects_non_normalized_or_disallowed_characters(
        string station,
        string register,
        string receipt,
        string field
    )
    {
        var errors = Validate(
            10m,
            5m,
            new ReceiptKey(station, register, new DateOnly(2026, 11, 21), receipt)
        );
        Assert.Contains(
            errors,
            error => error.Field == field && error.Code == LoyaltyErrorCodes.InvalidFormat
        );
    }

    [Fact]
    public void Requires_every_part_of_the_receipt_key()
    {
        var errors = Validate(10m, 5m, new ReceiptKey(" ", "", new DateOnly(2026, 11, 21), "  "));
        Assert.Contains(
            errors,
            error => error is { Field: "stationCode", Code: LoyaltyErrorCodes.RequiredField }
        );
        Assert.Contains(
            errors,
            error => error is { Field: "registerCode", Code: LoyaltyErrorCodes.RequiredField }
        );
        Assert.Contains(
            errors,
            error => error is { Field: "receiptNumber", Code: LoyaltyErrorCodes.RequiredField }
        );
    }

    [Fact]
    public void Rejects_too_long_receipt_parts()
    {
        var errors = Validate(
            10m,
            5m,
            new ReceiptKey(
                new string('S', 33),
                "K1",
                new DateOnly(2026, 11, 21),
                new string('9', 65)
            )
        );
        Assert.Contains(
            errors,
            error => error is { Field: "stationCode", Code: LoyaltyErrorCodes.TooLong }
        );
        Assert.Contains(
            errors,
            error => error is { Field: "receiptNumber", Code: LoyaltyErrorCodes.TooLong }
        );
    }
}

public sealed class SaleDateRuleTests
{
    private static readonly DateOnly Today = new(2027, 1, 15);

    private static IReadOnlyList<FuelClub.Api.Modules.Loyalty.Domain.LoyaltyValidationError> Validate(
        DateOnly saleDate
    ) =>
        FuelClub.Api.Modules.Loyalty.Domain.FuelPurchaseRules.Validate(
            new(10m, 1m, new("ST-01", "K1", saleDate, "R-1")),
            Today
        );

    [Fact]
    public void Today_and_past_dates_are_allowed()
    {
        Assert.Empty(Validate(Today));
        Assert.Empty(Validate(Today.AddYears(-5)));
    }

    [Fact]
    public void Tomorrow_is_rejected() =>
        Assert.Equal(
            [new("saleDate", FuelClub.Api.Modules.Loyalty.LoyaltyErrorCodes.SaleDateInFuture)],
            Validate(Today.AddDays(1))
        );
}

public sealed class StationClockTests
{
    [Theory]
    [InlineData("2027-01-15T22:59:59Z", "2027-01-15")] // 23:59:59 CET
    [InlineData("2027-01-15T23:00:00Z", "2027-01-16")] // 00:00 CET
    [InlineData("2026-07-01T21:59:59Z", "2026-07-01")] // 23:59:59 CEST
    [InlineData("2026-07-01T22:00:00Z", "2026-07-02")] // 00:00 CEST
    public void Today_follows_the_station_time_zone_including_daylight_saving(
        string utcNow,
        string expected
    )
    {
        var clock = new MutableTimeProvider();
        clock.Set(DateTimeOffset.Parse(utcNow, System.Globalization.CultureInfo.InvariantCulture));
        var stationClock = new FuelClub.Api.Modules.Loyalty.Infrastructure.StationClock(
            clock,
            Microsoft.Extensions.Options.Options.Create(
                new FuelClub.Api.Modules.Loyalty.Infrastructure.LoyaltyOptions()
            )
        );

        Assert.Equal(
            DateOnly.Parse(expected, System.Globalization.CultureInfo.InvariantCulture),
            stationClock.Today
        );
    }

    [Theory]
    [InlineData("Mars/Olympus", false)]
    [InlineData("", false)]
    [InlineData("Europe/Warsaw", true)]
    [InlineData("UTC", true)]
    public void Station_time_zone_must_exist(string zone, bool valid) =>
        Assert.Equal(
            valid,
            new FuelClub.Api.Modules.Loyalty.Infrastructure.LoyaltyOptionsValidator()
                .Validate(
                    null,
                    new FuelClub.Api.Modules.Loyalty.Infrastructure.LoyaltyOptions
                    {
                        StationTimeZone = zone,
                    }
                )
                .Succeeded
        );
}
