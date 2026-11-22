using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Loyalty.Points;

public enum SpendOutcome
{
    Spent,
    InsufficientPoints,
    CustomerBusy,
}

public sealed record SpendResult(SpendOutcome Outcome, int BalanceAfter = 0);

/// <summary>
/// The only way other modules take points from a customer. Runs in the caller's transaction:
/// customer lock -> conditional debit -> ledger entry. The caller commits (or rolls back) the
/// transaction, so the debit and whatever the caller saves next are atomic.
/// </summary>
public sealed class PointsSpending(AppDbContext db, CustomerLock customerLock)
{
    public async Task<SpendResult> SpendAsync(
        string userId,
        int points,
        PointsLedgerEntryType type,
        Guid sourceId,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(points);

        if (!await customerLock.TryAcquireAsync(userId, cancellationToken))
            return new SpendResult(SpendOutcome.CustomerBusy);

        // Debit only when the balance covers it; no account row means a balance of 0.
        var balances = await db
            .Database.SqlQuery<int>(
                $"""
                UPDATE "LoyaltyAccounts" SET "Balance" = "Balance" - {points}
                WHERE "UserId" = {userId} AND "Balance" >= {points}
                RETURNING "Balance" AS "Value"
                """
            )
            .ToListAsync(cancellationToken);
        if (balances.Count == 0)
            return new SpendResult(SpendOutcome.InsufficientPoints);

        db.PointsLedgerEntries.Add(
            new PointsLedgerEntry
            {
                Id = Guid.NewGuid(),
                UserId = userId,
                Delta = -points,
                Type = type,
                SourceId = sourceId,
                BalanceAfter = balances[0],
            }
        );
        await db.SaveChangesAsync(cancellationToken);
        return new SpendResult(SpendOutcome.Spent, balances[0]);
    }
}
