using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text.RegularExpressions;

namespace FuelClub.Api.Modules.Loyalty.Domain;

/// <summary>
/// Card identifying the recipient of points. The code carries no personal data and does not
/// authenticate anyone; at most one card per user is active (RevokedAt is null).
/// </summary>
public sealed class LoyaltyCard
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = null!;
    public string Code { get; set; } = null!;
    public DateTimeOffset IssuedAt { get; set; }
    public DateTimeOffset? RevokedAt { get; set; }
}

/// <summary>
/// 128 random bits as 26 Crockford Base32 characters (130 bits, so the first character is 0-7).
/// Codes are never logged.
/// </summary>
public static partial class LoyaltyCardCode
{
    public const int Length = 26;
    public const string Alphabet = "0123456789ABCDEFGHJKMNPQRSTVWXYZ";
    public const string Pattern = "^[0-7][0-9A-HJKMNP-TV-Z]{25}$";

    public static string Generate()
    {
        Span<byte> bytes = stackalloc byte[16];
        RandomNumberGenerator.Fill(bytes);
        return Encode(bytes);
    }

    public static string Encode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length != 16)
            throw new ArgumentException(
                "Card code is encoded from exactly 16 bytes.",
                nameof(bytes)
            );

        var value = BinaryPrimitives.ReadUInt128BigEndian(bytes);
        Span<char> chars = stackalloc char[Length];
        for (var i = Length - 1; i >= 0; i--)
        {
            chars[i] = Alphabet[(int)(value & 31)];
            value >>= 5;
        }
        return new string(chars);
    }

    /// <summary>
    /// Canonical form of a typed or scanned code: separators removed, upper case, and the Crockford
    /// look-alikes O -> 0, I/L -> 1. The result still has to pass <see cref="IsValid"/>.
    /// </summary>
    public static string Normalize(string? input)
    {
        var builder = new System.Text.StringBuilder(Length);
        foreach (var raw in (input ?? string.Empty).Trim())
        {
            if (raw is '-' or ' ')
                continue;
            builder.Append(
                char.ToUpperInvariant(raw) switch
                {
                    'O' => '0',
                    'I' or 'L' => '1',
                    var other => other,
                }
            );
        }
        return builder.ToString();
    }

    public static bool IsValid(string? code) =>
        code is not null && CodeRegex().IsMatch(code) && code.Length == Length;

    [GeneratedRegex(Pattern, RegexOptions.CultureInvariant)]
    private static partial Regex CodeRegex();
}
