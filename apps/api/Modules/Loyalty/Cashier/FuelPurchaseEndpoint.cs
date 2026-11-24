using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace FuelClub.Api.Modules.Loyalty.Cashier;

/// <summary>Station and register are not part of the request: they come from the cashier's assignment.</summary>
public sealed record FuelPurchaseRequest(
    string? CardCode,
    decimal Amount,
    decimal Liters,
    DateOnly SaleDate,
    string? ReceiptNumber,
    string? CouponCode = null
);

/// <summary>
/// The cashier learns the purchase, the discount and the points, never the customer's balance.
/// Without a coupon the discount is 0 and the paid amount is the gross amount.
/// </summary>
public sealed record FuelPurchaseResponse(
    Guid PurchaseId,
    int PointsAwarded,
    decimal Discount,
    decimal PaidAmount
);

/// <summary>
/// Runs inside the idempotency transaction. Order: (reservation by the filter) -> validation -> card
/// lookup -> customer lock of the card owner -> card re-check -> receipt check -> coupon hold ->
/// purchase -> coupon redeemed -> balance, ledger. A coupon error therefore never hides a duplicate
/// receipt, and nothing is written before the coupon is known to be usable.
/// The re-check after the lock decides the race with a card rotation: a rotation committed first
/// makes the old code fail; a purchase holding the lock first makes the rotation wait.
/// </summary>
public static class FuelPurchaseEndpoint
{
    public const string ReceiptConstraint = "UX_FuelPurchases_Receipt";

    public const string Description =
        "Registers a fuel purchase for the scanned card. An optional couponCode (omitted or null = no "
        + "coupon) redeems the card owner's active coupon on this purchase: the server computes the "
        + "discount, points are awarded on the paid amount.";

    public static async Task<Results<Created<FuelPurchaseResponse>, ProblemHttpResult>> Handle(
        FuelPurchaseRequest request,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CustomerLock customerLock,
        ICouponRedemption coupons,
        StationClock stationClock,
        CancellationToken cancellationToken
    )
    {
        var cashierId = users.GetUserId(http.User)!;

        // 400 only before any change: idempotency does not store it, so a corrected request may reuse the key.
        var cardCode = LoyaltyCardCode.Normalize(request.CardCode);
        var errors = new List<IFieldError>();
        if (string.IsNullOrWhiteSpace(request.CardCode))
            errors.Add(new LoyaltyValidationError("cardCode", LoyaltyErrorCodes.RequiredField));
        else if (!LoyaltyCardCode.IsValid(cardCode))
            errors.Add(new LoyaltyValidationError("cardCode", LoyaltyErrorCodes.InvalidFormat));

        // Only an omitted or null code means "no coupon"; a blank or malformed one is an error.
        string? couponCode = null;
        if (request.CouponCode is { } typedCoupon)
        {
            if (coupons.Validate("couponCode", typedCoupon, out var normalized) is { } couponError)
                errors.Add(couponError);
            else
                couponCode = normalized;
        }

        var station = await db
            .CashierStations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.UserId == cashierId, cancellationToken);
        if (station is null)
            return ApiProblems.Problem(
                StatusCodes.Status404NotFound,
                "Cashier station not assigned",
                LoyaltyErrorCodes.CashierStationMissing
            );

        var input = FuelPurchaseRules.Normalize(
            new FuelPurchaseInput(
                request.Amount,
                request.Liters,
                new ReceiptKey(
                    station.StationCode,
                    station.RegisterCode,
                    request.SaleDate,
                    request.ReceiptNumber ?? string.Empty
                )
            )
        );
        errors.AddRange(FuelPurchaseRules.Validate(input, stationClock.Today));
        if (errors.Count > 0)
            return ApiProblems.Validation(
                "Purchase is not valid",
                LoyaltyErrorCodes.ValidationFailed,
                errors
            );

        var card = await db
            .LoyaltyCards.AsNoTracking()
            .Where(c => c.Code == cardCode && c.RevokedAt == null)
            .Select(c => new { c.Id, c.UserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (card is null)
            return LoyaltyProblems.CardNotFound();

        if (!await customerLock.TryAcquireAsync(card.UserId, cancellationToken))
            return LoyaltyProblems.CustomerBusy(http);

        // A new statement after the lock sees rotations committed while this request waited.
        if (
            !await db.LoyaltyCards.AnyAsync(
                c => c.Id == card.Id && c.RevokedAt == null,
                cancellationToken
            )
        )
            return LoyaltyProblems.CardNotFound();

        // Purchases of one customer are serialized by the lock; across customers UNIQUE decides below.
        if (
            await db.FuelPurchases.AnyAsync(
                p =>
                    p.StationCode == input.Receipt.StationCode
                    && p.RegisterCode == input.Receipt.RegisterCode
                    && p.SaleDate == input.Receipt.SaleDate
                    && p.ReceiptNumber == input.Receipt.ReceiptNumber,
                cancellationToken
            )
        )
            return ReceiptAlreadyRegistered();

        CouponHold? hold = null;
        if (couponCode is not null)
        {
            var held = await coupons.HoldAsync(
                http,
                card.UserId,
                couponCode,
                input.Liters,
                input.Amount,
                cancellationToken
            );
            if (held.Problem is { } problem)
                return problem;
            hold = held.Hold!;
        }

        var discount = hold?.Discount ?? 0m;
        var paidAmount = hold?.PaidAmount ?? input.Amount;
        var points = PointsPolicy.ForPaidAmount(paidAmount);
        var purchase = new FuelPurchase
        {
            Id = Guid.NewGuid(),
            UserId = card.UserId,
            StationCode = input.Receipt.StationCode,
            RegisterCode = input.Receipt.RegisterCode,
            SaleDate = input.Receipt.SaleDate,
            ReceiptNumber = input.Receipt.ReceiptNumber,
            GrossAmount = input.Amount,
            Discount = discount,
            PaidAmount = paidAmount,
            Liters = input.Liters,
            PointsAwarded = points,
            RegisteredByUserId = cashierId,
        };
        db.FuelPurchases.Add(purchase);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException
                    is PostgresException
                    {
                        SqlState: PostgresErrorCodes.UniqueViolation,
                        ConstraintName: ReceiptConstraint
                    }
            )
        {
            return ReceiptAlreadyRegistered();
        }

        if (hold is not null)
            await coupons.MarkRedeemedAsync(
                hold.CouponId,
                card.UserId,
                purchase.Id,
                cancellationToken
            );

        // Below 1 zł: the purchase is recorded, balance and history stay unchanged.
        if (points > 0)
        {
            var balanceAfter = await CreditAsync(db, card.UserId, points, cancellationToken);
            db.PointsLedgerEntries.Add(
                new PointsLedgerEntry
                {
                    Id = Guid.NewGuid(),
                    UserId = card.UserId,
                    Delta = points,
                    Type = PointsLedgerEntryType.FuelPurchase,
                    SourceId = purchase.Id,
                    BalanceAfter = balanceAfter,
                }
            );
            await db.SaveChangesAsync(cancellationToken);
        }

        Notifications.LoyaltyNotifier.AfterResponse(
            http,
            card.UserId,
            new Notifications.PointsAwardedEvent(
                purchase.Id,
                points,
                purchase.GrossAmount,
                discount,
                paidAmount,
                purchase.Liters,
                purchase.SaleDate,
                purchase.StationCode
            )
        );
        return TypedResults.Created(
            (string?)null,
            new FuelPurchaseResponse(purchase.Id, points, discount, paidAmount)
        );
    }

    /// <summary>No owner information: the receipt may belong to another customer.</summary>
    private static ProblemHttpResult ReceiptAlreadyRegistered() =>
        ApiProblems.Problem(
            StatusCodes.Status409Conflict,
            "Receipt already registered",
            LoyaltyErrorCodes.ReceiptAlreadyRegistered
        );

    /// <summary>
    /// One statement: adds to the existing balance or creates the account, locks the row until the
    /// transaction ends and returns the balance after the change (for BalanceAfter).
    /// </summary>
    private static async Task<int> CreditAsync(
        AppDbContext db,
        string userId,
        int points,
        CancellationToken cancellationToken
    )
    {
        var balances = await db
            .Database.SqlQuery<int>(
                $"""
                INSERT INTO "LoyaltyAccounts" ("UserId", "Balance") VALUES ({userId}, {points})
                ON CONFLICT ("UserId") DO UPDATE SET "Balance" = "LoyaltyAccounts"."Balance" + EXCLUDED."Balance"
                RETURNING "Balance" AS "Value"
                """
            )
            .ToListAsync(cancellationToken);
        return balances.Single();
    }
}
