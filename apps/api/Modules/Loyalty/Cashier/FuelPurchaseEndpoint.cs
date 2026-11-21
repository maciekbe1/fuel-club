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
    string? ReceiptNumber);

/// <summary>The cashier learns the purchase ID and the points awarded, never the customer's balance.</summary>
public sealed record FuelPurchaseResponse(Guid PurchaseId, int PointsAwarded);

/// <summary>
/// Runs inside the idempotency transaction. Order: (reservation by the filter) -> validation -> card
/// lookup -> customer lock of the card owner -> card re-check -> purchase, balance, ledger.
/// The re-check after the lock decides the race with a card rotation: a rotation committed first
/// makes the old code fail; a purchase holding the lock first makes the rotation wait.
/// </summary>
public static class FuelPurchaseEndpoint
{
    public const string ReceiptConstraint = "UX_FuelPurchases_Receipt";

    public static async Task<Results<Created<FuelPurchaseResponse>, ProblemHttpResult>> Handle(
        FuelPurchaseRequest request,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CustomerLock customerLock,
        StationClock stationClock,
        CancellationToken cancellationToken)
    {
        var cashierId = users.GetUserId(http.User)!;

        // 400 only before any change: idempotency does not store it, so a corrected request may reuse the key.
        var cardCode = LoyaltyCardCode.Normalize(request.CardCode);
        var errors = new List<LoyaltyValidationError>();
        if (string.IsNullOrWhiteSpace(request.CardCode))
            errors.Add(new("cardCode", LoyaltyErrorCodes.RequiredField));
        else if (!LoyaltyCardCode.IsValid(cardCode))
            errors.Add(new("cardCode", LoyaltyErrorCodes.InvalidFormat));

        var station = await db.CashierStations.AsNoTracking()
            .SingleOrDefaultAsync(row => row.UserId == cashierId, cancellationToken);
        if (station is null)
            return Problem(StatusCodes.Status404NotFound, "Cashier station not assigned", LoyaltyErrorCodes.CashierStationMissing);

        var input = FuelPurchaseRules.Normalize(new FuelPurchaseInput(
            request.Amount,
            request.Liters,
            new ReceiptKey(station.StationCode, station.RegisterCode, request.SaleDate, request.ReceiptNumber ?? string.Empty)));
        errors.AddRange(FuelPurchaseRules.Validate(input, stationClock.Today));
        if (errors.Count > 0)
            return ValidationProblem(errors);

        var card = await db.LoyaltyCards.AsNoTracking()
            .Where(c => c.Code == cardCode && c.RevokedAt == null)
            .Select(c => new { c.Id, c.UserId })
            .SingleOrDefaultAsync(cancellationToken);
        if (card is null)
            return CardNotFound();

        if (!await customerLock.TryAcquireAsync(card.UserId, cancellationToken))
            return CustomerBusy(http);

        // A new statement after the lock sees rotations committed while this request waited.
        if (!await db.LoyaltyCards.AnyAsync(c => c.Id == card.Id && c.RevokedAt == null, cancellationToken))
            return CardNotFound();

        // No coupon yet: nothing is discounted, the customer pays the gross amount.
        var points = PointsPolicy.ForPaidAmount(input.Amount);
        var purchase = new FuelPurchase
        {
            Id = Guid.NewGuid(),
            UserId = card.UserId,
            StationCode = input.Receipt.StationCode,
            RegisterCode = input.Receipt.RegisterCode,
            SaleDate = input.Receipt.SaleDate,
            ReceiptNumber = input.Receipt.ReceiptNumber,
            GrossAmount = input.Amount,
            Discount = 0m,
            PaidAmount = input.Amount,
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
            when (exception.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation, ConstraintName: ReceiptConstraint })
        {
            // No owner information: the receipt may belong to another customer.
            return Problem(StatusCodes.Status409Conflict, "Receipt already registered", LoyaltyErrorCodes.ReceiptAlreadyRegistered);
        }

        // Below 1 zł: the purchase is recorded, balance and history stay unchanged.
        if (points > 0)
        {
            var balanceAfter = await CreditAsync(db, card.UserId, points, cancellationToken);
            db.PointsLedgerEntries.Add(new PointsLedgerEntry
            {
                Id = Guid.NewGuid(),
                UserId = card.UserId,
                Delta = points,
                Type = PointsLedgerEntryType.FuelPurchase,
                SourceId = purchase.Id,
                BalanceAfter = balanceAfter,
            });
            await db.SaveChangesAsync(cancellationToken);
        }

        return TypedResults.Created((string?)null, new FuelPurchaseResponse(purchase.Id, points));
    }

    /// <summary>
    /// One statement: adds to the existing balance or creates the account, locks the row until the
    /// transaction ends and returns the balance after the change (for BalanceAfter).
    /// </summary>
    private static async Task<int> CreditAsync(AppDbContext db, string userId, int points, CancellationToken cancellationToken)
    {
        var balances = await db.Database.SqlQuery<int>(
            $"""
            INSERT INTO "LoyaltyAccounts" ("UserId", "Balance") VALUES ({userId}, {points})
            ON CONFLICT ("UserId") DO UPDATE SET "Balance" = "LoyaltyAccounts"."Balance" + EXCLUDED."Balance"
            RETURNING "Balance" AS "Value"
            """).ToListAsync(cancellationToken);
        return balances.Single();
    }

    private static ProblemHttpResult CardNotFound() =>
        Problem(StatusCodes.Status404NotFound, "Loyalty card not found", LoyaltyErrorCodes.CardNotFound);

    /// <summary>Transient and not stored by idempotency (5xx): the client retries with the same key.</summary>
    private static ProblemHttpResult CustomerBusy(HttpContext http)
    {
        http.Response.Headers.RetryAfter = "1";
        return Problem(StatusCodes.Status503ServiceUnavailable, "Another operation on this customer is in progress", LoyaltyErrorCodes.CustomerBusy);
    }

    private static ProblemHttpResult ValidationProblem(IEnumerable<LoyaltyValidationError> errors) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status400BadRequest,
            title: "Purchase is not valid",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = LoyaltyErrorCodes.ValidationFailed,
                ["errors"] = errors.GroupBy(error => error.Field)
                    .ToDictionary(group => group.Key, group => group.Select(error => new { error.Field, error.Code }).ToArray()),
            });

    private static ProblemHttpResult Problem(int status, string title, string code) =>
        TypedResults.Problem(
            statusCode: status,
            title: title,
            extensions: new Dictionary<string, object?> { ["code"] = code });
}
