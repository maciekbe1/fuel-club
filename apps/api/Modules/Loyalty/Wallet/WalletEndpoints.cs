using System.Text;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Loyalty.Wallet;

public sealed record WalletResponse(int Balance);

/// <summary>
/// Fuel purchase behind a history entry; null for entries of other types. <c>Amount</c> is the gross
/// amount, kept for compatibility; <c>PaidAmount</c> = <c>GrossAmount</c> - <c>Discount</c>.
/// </summary>
public sealed record TransactionFuelPurchase(
    Guid PurchaseId,
    string StationCode,
    DateOnly SaleDate,
    decimal Amount,
    decimal Liters,
    decimal GrossAmount,
    decimal Discount,
    decimal PaidAmount
);

/// <summary>Coupon bought by a CouponExchange entry; null for entries of other types.</summary>
public sealed record TransactionCouponExchange(Guid CouponId, string OfferName);

public sealed record TransactionItem(
    Guid Id,
    string Type,
    int Delta,
    int BalanceAfter,
    DateTimeOffset CreatedAt,
    TransactionFuelPurchase? FuelPurchase,
    TransactionCouponExchange? CouponExchange
);

public sealed record TransactionsResponse(IReadOnlyList<TransactionItem> Items, string? NextCursor);

/// <summary>Read-only views of the logged-in customer's own points. No user parameter exists.</summary>
public static class WalletEndpoints
{
    public const int DefaultLimit = 20;
    public const int MaxLimit = 100;
    private const string CursorPrefix = "v1:";

    public const string TransactionsDescription =
        "History of points changes (ledger), newest first. Purchases that awarded 0 points (below 1 zł) "
        + "have no ledger entry and do not appear here, including a purchase fully paid with a coupon. "
        + "A purchase shows amount (gross, kept for compatibility), grossAmount, discount and paidAmount. Pages follow the ledger sequence: the next page "
        + "returns entries older than the cursor; entries added later show up after reloading the first page.";

    public static async Task<Ok<WalletResponse>> GetWallet(
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var userId = users.GetUserId(http.User)!;
        return TypedResults.Ok(
            new WalletResponse(await BalanceAsync(db, userId, cancellationToken))
        );
    }

    /// <summary>The balance; also read by the assistant's get_my_balance.</summary>
    public static async Task<int> BalanceAsync(
        AppDbContext db,
        string userId,
        CancellationToken cancellationToken
    )
    {
        var balance = await db
            .LoyaltyAccounts.AsNoTracking()
            .Where(account => account.UserId == userId)
            .Select(account => (int?)account.Balance)
            .SingleOrDefaultAsync(cancellationToken);
        // No account yet means nothing was ever credited.
        return balance ?? 0;
    }

    public static async Task<Results<Ok<TransactionsResponse>, ProblemHttpResult>> GetTransactions(
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        int? limit,
        string? cursor,
        CancellationToken cancellationToken
    )
    {
        var errors = new List<LoyaltyValidationError>();
        var pageSize = limit ?? DefaultLimit;
        if (pageSize is < 1 or > MaxLimit)
            errors.Add(new("limit", LoyaltyErrorCodes.LimitOutOfRange));
        long? before = null;
        if (cursor is not null)
        {
            if (TryDecodeCursor(cursor, out var sequence))
                before = sequence;
            else
                errors.Add(new("cursor", LoyaltyErrorCodes.InvalidCursor));
        }
        if (errors.Count > 0)
            return LoyaltyProblems.Validation("Request is not valid", errors);

        var userId = users.GetUserId(http.User)!;
        var entries = db.PointsLedgerEntries.AsNoTracking().Where(entry => entry.UserId == userId);
        if (before is { } last)
            entries = entries.Where(entry => entry.Sequence < last);

        // The linked purchase must belong to the same user, not just match the SourceId.
        // Same for the coupon of an exchange (read from the Rewards table in the shared database).
        var ownPurchases = db
            .FuelPurchases.AsNoTracking()
            .Where(purchase => purchase.UserId == userId);
        var ownCoupons = db.Coupons.AsNoTracking().Where(coupon => coupon.UserId == userId);
        var rows = await (
            from entry in entries
            join purchase in ownPurchases on entry.SourceId equals purchase.Id into matches
            from purchase in matches.DefaultIfEmpty()
            join coupon in ownCoupons on entry.SourceId equals coupon.Id into couponMatches
            from coupon in couponMatches.DefaultIfEmpty()
            orderby entry.Sequence descending
            select new
            {
                entry.Id,
                entry.Type,
                entry.Delta,
                entry.BalanceAfter,
                entry.CreatedAt,
                entry.Sequence,
                PurchaseId = (Guid?)purchase.Id,
                purchase.StationCode,
                SaleDate = (DateOnly?)purchase.SaleDate,
                Amount = (decimal?)purchase.GrossAmount,
                Liters = (decimal?)purchase.Liters,
                Discount = (decimal?)purchase.Discount,
                PaidAmount = (decimal?)purchase.PaidAmount,
                CouponId = (Guid?)coupon.Id,
                coupon.OfferName,
            }
        )
            // One extra row tells whether another page exists.
            .Take(pageSize + 1)
            .ToListAsync(cancellationToken);

        var page = rows.Take(pageSize).ToList();
        var items = page.Select(row => new TransactionItem(
                row.Id,
                row.Type.ToString(),
                row.Delta,
                row.BalanceAfter,
                row.CreatedAt,
                row.Type == PointsLedgerEntryType.FuelPurchase && row.PurchaseId is { } purchaseId
                    ? new TransactionFuelPurchase(
                        purchaseId,
                        row.StationCode!,
                        row.SaleDate!.Value,
                        row.Amount!.Value,
                        row.Liters!.Value,
                        row.Amount!.Value,
                        row.Discount!.Value,
                        row.PaidAmount!.Value
                    )
                    : null,
                row.Type == PointsLedgerEntryType.CouponExchange && row.CouponId is { } couponId
                    ? new TransactionCouponExchange(couponId, row.OfferName!)
                    : null
            ))
            .ToList();
        var nextCursor = rows.Count > pageSize ? EncodeCursor(page[^1].Sequence) : null;
        return TypedResults.Ok(new TransactionsResponse(items, nextCursor));
    }

    public static string EncodeCursor(long sequence) =>
        WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes(
                CursorPrefix + sequence.ToString(System.Globalization.CultureInfo.InvariantCulture)
            )
        );

    private static bool TryDecodeCursor(string cursor, out long sequence)
    {
        sequence = 0;
        try
        {
            var text = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
            return text.StartsWith(CursorPrefix, StringComparison.Ordinal)
                && long.TryParse(
                    text[CursorPrefix.Length..],
                    System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture,
                    out sequence
                )
                && sequence > 0;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
