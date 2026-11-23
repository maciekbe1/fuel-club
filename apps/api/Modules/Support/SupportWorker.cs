using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Support.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Support;

public sealed record HousekeepingResult(int Returned, int Expired, int Purged);

/// <summary>
/// No case stays open forever (ADR 0012): an assigned case whose agent was idle (no claim or agent
/// message; customer messages do not count) goes back to the queue, a waiting case
/// without activity expires, and closed or expired cases are deleted after the retention. Each change
/// is a conditional UPDATE on the stale condition, so a message written meanwhile (which moves
/// LastActivityAt under the same row lock) keeps its case.
/// </summary>
public sealed class SupportHousekeeping(AppDbContext db, IOptions<SupportOptions> options)
{
    public async Task<HousekeepingResult> RunAsync(DateTimeOffset now, CancellationToken ct)
    {
        var o = options.Value;
        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        // Expire first: a case returned to the queue below has fresh activity anyway.
        var expiredBefore = now - o.WaitingExpiry;
        var expired = await db
            .Database.SqlQuery<Guid>(
                $"""
                UPDATE "SupportCases" SET "Status" = 'Expired', "ClosedAt" = clock_timestamp()
                WHERE "Status" = 'Waiting' AND "LastActivityAt" < {expiredBefore}
                RETURNING "Id" AS "Value"
                """
            )
            .ToListAsync(ct);
        foreach (var id in expired)
            await SupportCases.AppendAsync(
                db,
                id,
                [new NewSupportMessage(SupportAuthor.System, SupportSystemEvents.Expired)],
                ct
            );

        var idleBefore = now - o.AssignedIdleTimeout;
        var returned = await db
            .Database.SqlQuery<Guid>(
                $"""
                UPDATE "SupportCases" SET "Status" = 'Waiting', "AssignedToUserId" = NULL
                WHERE "Status" = 'Assigned' AND "AgentActivityAt" < {idleBefore}
                RETURNING "Id" AS "Value"
                """
            )
            .ToListAsync(ct);
        foreach (var id in returned)
            await SupportCases.AppendAsync(
                db,
                id,
                [new NewSupportMessage(SupportAuthor.System, SupportSystemEvents.IdleReturned)],
                ct
            );

        // Messages go with the case (ON DELETE CASCADE).
        var purgeBefore = now - o.Retention;
        var purged = await db
            .SupportCases.Where(c =>
                (c.Status == SupportCaseStatus.Closed || c.Status == SupportCaseStatus.Expired)
                && c.ClosedAt < purgeBefore
            )
            .ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);
        return new HousekeepingResult(returned.Count, expired.Count, purged);
    }
}

/// <summary>Runs <see cref="SupportHousekeeping"/> every Support:WorkerInterval. Logs counts only, never content.</summary>
public sealed class SupportWorker(
    IServiceScopeFactory scopes,
    IOptions<SupportOptions> options,
    TimeProvider clock,
    ILogger<SupportWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!options.Value.WorkerEnabled)
            return;
        using var timer = new PeriodicTimer(options.Value.WorkerInterval, clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var result = await scope
                    .ServiceProvider.GetRequiredService<SupportHousekeeping>()
                    .RunAsync(clock.GetUtcNow(), stop);
                if (result.Returned + result.Expired + result.Purged > 0)
                    logger.LogInformation(
                        "Support housekeeping: {Returned} back in the queue, {Expired} expired, {Purged} deleted",
                        result.Returned,
                        result.Expired,
                        result.Purged
                    );
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogWarning("Support housekeeping failed: {Error}", error.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false));
    }
}
