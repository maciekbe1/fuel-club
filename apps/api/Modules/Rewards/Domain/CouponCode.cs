using System.Security.Cryptography;
using System.Text.RegularExpressions;
using FuelClub.Api.Modules.Loyalty.Domain;

namespace FuelClub.Api.Modules.Rewards.Domain;

/// <summary>
/// "K" + 128 random bits as 26 Crockford Base32 characters (same encoding as a card code, so the
/// first random character is 0-7). 27 characters in total. The prefix helps a scanner tell a coupon
/// from a card; the server still checks type, owner and status. Codes are never logged.
/// </summary>
public static partial class CouponCode
{
    public const char Prefix = 'K';
    public const int Length = 1 + LoyaltyCardCode.Length;
    public const string Pattern = "^K[0-7][0-9A-HJKMNP-TV-Z]{25}$";

    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Prefix + LoyaltyCardCode.Encode(bytes);
    }

    /// <summary>Same canonical form as a card code (separators removed, upper case, O/I/L look-alikes).</summary>
    public static string Normalize(string? input) => LoyaltyCardCode.Normalize(input);

    public static bool IsValid(string? code) =>
        code is not null && code.Length == Length && CodeRegex().IsMatch(code);

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex CodeRegex();
}
