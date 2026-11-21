using FuelClub.Api.Modules.Loyalty.Domain;

namespace FuelClub.Api.Tests;

public sealed class LoyaltyCardCodeTests
{
    [Fact]
    public void Generated_codes_are_valid_and_distinct()
    {
        var codes = Enumerable.Range(0, 1000).Select(_ => LoyaltyCardCode.Generate()).ToList();

        Assert.All(codes, code => Assert.True(LoyaltyCardCode.IsValid(code), code));
        Assert.Equal(codes.Count, codes.Distinct().Count());
    }

    [Fact]
    public void Encoding_covers_all_128_bits()
    {
        Assert.Equal(new string('0', 26), LoyaltyCardCode.Encode(new byte[16]));
        Assert.Equal("7" + new string('Z', 25), LoyaltyCardCode.Encode(Enumerable.Repeat((byte)0xFF, 16).ToArray()));
        var lastBit = new byte[16];
        lastBit[15] = 1;
        Assert.Equal(new string('0', 25) + "1", LoyaltyCardCode.Encode(lastBit));
    }

    [Theory]
    [InlineData("7zzz-zzzz-zzzz-zzzz-zzzz-zzzz-zz", "7ZZZZZZZZZZZZZZZZZZZZZZZZZ")]
    [InlineData("  0000 0000 0000 0000 0000 0000 01 ", "00000000000000000000000001")]
    [InlineData("OOOOOOOOOOOOOOOOOOOOOOOOIL", "00000000000000000000000011")]
    public void Normalize_accepts_typed_variants(string input, string expected)
    {
        Assert.Equal(expected, LoyaltyCardCode.Normalize(input));
        Assert.True(LoyaltyCardCode.IsValid(LoyaltyCardCode.Normalize(input)));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("0000000000000000000000000")]
    [InlineData("000000000000000000000000000")]
    [InlineData("80000000000000000000000000")]
    [InlineData("0000000000000000000000000U")]
    [InlineData("0000000000000000000000000z")]
    [InlineData("0000000000000000000000000\n")]
    [InlineData("00000000000000000000000000\n")]
    public void IsValid_rejects_non_canonical_codes(string? code) =>
        Assert.False(LoyaltyCardCode.IsValid(code));
}
