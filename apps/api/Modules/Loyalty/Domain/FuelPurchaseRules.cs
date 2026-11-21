using System.Text.RegularExpressions;

namespace FuelClub.Api.Modules.Loyalty.Domain;

public sealed record LoyaltyValidationError(string Field, string Code) : FuelClub.Api.Infrastructure.IFieldError;

/// <summary>
/// Business rules for a fuel purchase. The database repeats range, date and format checks
/// as a last line of defence.
/// Usage: <c>var input = FuelPurchaseRules.Normalize(raw); var errors = FuelPurchaseRules.Validate(input, stationToday);</c>
/// Validate rejects non-normalized receipt parts, so skipping Normalize fails safely.
/// </summary>
public static partial class FuelPurchaseRules
{
    public const decimal MaxAmount = 5000m;
    public const int AmountPrecision = 10;
    public const int AmountScale = 2;

    /// <summary>Largest value that fits numeric(10,3).</summary>
    public const decimal MaxLiters = 9_999_999.999m;
    public const int LitersPrecision = 10;
    public const int LitersScale = 3;

    public const int CodeMaxLength = 32;
    public const int ReceiptNumberMaxLength = 64;

    /// <summary>Allowed characters after normalization. Shared with the database CHECK constraints.</summary>
    public const string CodePattern = "^[A-Z0-9-]+$";
    public const string ReceiptNumberPattern = "^[A-Z0-9/-]+$";

    public static FuelPurchaseInput Normalize(FuelPurchaseInput input) =>
        input with
        {
            Receipt = input.Receipt with
            {
                StationCode = NormalizeCode(input.Receipt.StationCode),
                RegisterCode = NormalizeCode(input.Receipt.RegisterCode),
                ReceiptNumber = NormalizeCode(input.Receipt.ReceiptNumber),
            },
        };

/// <param name="stationToday">Current date in the station's time zone; later sale dates are rejected.</param>
    public static IReadOnlyList<LoyaltyValidationError> Validate(FuelPurchaseInput input, DateOnly stationToday)
    {
        var errors = new List<LoyaltyValidationError>();

        if (input.Amount <= 0m || input.Amount > MaxAmount)
            errors.Add(new("amount", LoyaltyErrorCodes.AmountOutOfRange));
        else if (!HasAtMostDecimals(input.Amount, AmountScale))
            errors.Add(new("amount", LoyaltyErrorCodes.AmountPrecision));

        if (input.Liters <= 0m || input.Liters > MaxLiters)
            errors.Add(new("liters", LoyaltyErrorCodes.LitersOutOfRange));
        else if (!HasAtMostDecimals(input.Liters, LitersScale))
            errors.Add(new("liters", LoyaltyErrorCodes.LitersPrecision));

        if (input.Receipt.SaleDate == default)
            errors.Add(new("saleDate", LoyaltyErrorCodes.RequiredField));
        else if (input.Receipt.SaleDate > stationToday)
            errors.Add(new("saleDate", LoyaltyErrorCodes.SaleDateInFuture));

        ValidateCode(errors, "stationCode", input.Receipt.StationCode, CodeMaxLength, CodeRegex());
        ValidateCode(
            errors,
            "registerCode",
            input.Receipt.RegisterCode,
            CodeMaxLength,
            CodeRegex()
        );
        ValidateCode(
            errors,
            "receiptNumber",
            input.Receipt.ReceiptNumber,
            ReceiptNumberMaxLength,
            ReceiptNumberRegex()
        );

        return errors;
    }

    public static bool HasAtMostDecimals(decimal value, int decimals) =>
        value == decimal.Round(value, decimals);

    public static string NormalizeCode(string? value) =>
        (value ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Validates a cashier station assignment with the same rules as the receipt key.</summary>
    public static IReadOnlyList<LoyaltyValidationError> ValidateStation(
        string? stationCode,
        string? registerCode
    )
    {
        var errors = new List<LoyaltyValidationError>();
        ValidateCode(errors, "stationCode", stationCode, CodeMaxLength, CodeRegex());
        ValidateCode(errors, "registerCode", registerCode, CodeMaxLength, CodeRegex());
        return errors;
    }

    private static void ValidateCode(
        List<LoyaltyValidationError> errors,
        string field,
        string? value,
        int maxLength,
        Regex pattern
    )
    {
        if (string.IsNullOrWhiteSpace(value))
            errors.Add(new(field, LoyaltyErrorCodes.RequiredField));
        else if (value.Length > maxLength)
            errors.Add(new(field, LoyaltyErrorCodes.TooLong));
        // The identity check is required: in .NET regex '$' also matches before a trailing '\n',
        // so the pattern alone would accept "ST-001\n".
        else if (value != NormalizeCode(value) || !pattern.IsMatch(value))
            errors.Add(new(field, LoyaltyErrorCodes.InvalidFormat));
    }

    [GeneratedRegex(CodePattern, RegexOptions.CultureInvariant)]
    private static partial Regex CodeRegex();

    [GeneratedRegex(ReceiptNumberPattern, RegexOptions.CultureInvariant)]
    private static partial Regex ReceiptNumberRegex();
}

/// <summary>Identifies a sale. The receipt number alone is not unique, not even within one station.</summary>
public sealed record ReceiptKey(
    string StationCode,
    string RegisterCode,
    DateOnly SaleDate,
    string ReceiptNumber
);

public sealed record FuelPurchaseInput(decimal Amount, decimal Liters, ReceiptKey Receipt);
