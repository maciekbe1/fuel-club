using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant.Tracing;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Observability;

public sealed record CountItem(string Key, int Count);

public sealed record ToolCount(string Name, string Status, int Count);

public sealed record ChatMetrics(
    int Turns,
    int Done,
    int Errors,
    int Cancelled,
    double? P95FirstEventMs,
    double? P95TotalMs,
    List<CountItem> ErrorCodes,
    List<ToolCount> Tools,
    long? InputTokens,
    long? OutputTokens,
    int TurnsWithoutTokenData
);

public sealed record ObservabilitySummary(
    DateTimeOffset From,
    DateTimeOffset To,
    ChatMetrics Chat,
    List<CountItem> Security,
    List<AnomalyResult> Anomalies
);

public sealed record ReviewTool(string Name, string Status, int DurationMs);

public sealed record ReviewItem(
    Guid Id,
    string CorrelationId,
    string UserPseudonym,
    DateTimeOffset StartedAt,
    string Outcome,
    string? ErrorCode,
    string Provider,
    string Model,
    string PromptVersion,
    int TotalMs,
    List<ReviewTool> Tools,
    List<string> Reasons
);

public sealed record ReviewQueue(List<ReviewItem> Items);

public sealed record ObservabilityValidationError(string Field, string Code) : IFieldError;

/// <summary>
/// Admin view of the metadata: aggregates, rules and the review queue. Never message content
/// (there is none stored), codes or user ids - pseudonyms only.
/// </summary>
public static class ObservabilityEndpoints
{
    public const string ValidationFailed = "observability.validation_failed";
    public const string OutOfRange = "observability.out_of_range";
    public const string TurnNotFound = "observability.turn_not_found";

    private sealed record Percentiles(double? First, double? Total);

    private sealed record ToolRow(string Name, string Status, int Count);

    public static async Task<Results<Ok<ObservabilitySummary>, ProblemHttpResult>> Summary(
        AppDbContext db,
        AnomalyRules rules,
        TimeProvider clock,
        int? hours,
        CancellationToken ct
    )
    {
        var span = hours ?? 24;
        if (span is < 1 or > 24 * 30)
            return ApiProblems.Validation(
                "Window out of range",
                ValidationFailed,
                [new ObservabilityValidationError("hours", OutOfRange)]
            );
        var to = clock.GetUtcNow();
        var from = to - TimeSpan.FromHours(span);

        var turns = db.AssistantTurns.Where(t => t.StartedAt > from && t.StartedAt <= to);
        var outcomes = await turns
            .GroupBy(t => t.Outcome)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);
        int Outcome(TurnOutcome o) => outcomes.FirstOrDefault(x => x.Key == o)?.Count ?? 0;

        var errorCodes = (
            await turns
                .Where(t => t.ErrorCode != null)
                .GroupBy(t => t.ErrorCode!)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToListAsync(ct)
        )
            .OrderByDescending(c => c.Count)
            .Select(c => new CountItem(c.Key, c.Count))
            .ToList();

        var percentiles = await db
            .Database.SqlQuery<Percentiles>(
                $"""
                SELECT percentile_cont(0.95) WITHIN GROUP (ORDER BY "FirstEventMs") AS "First",
                       percentile_cont(0.95) WITHIN GROUP (ORDER BY "TotalMs") AS "Total"
                FROM assistant_turns WHERE "StartedAt" > {from} AND "StartedAt" <= {to}
                """
            )
            .SingleAsync(ct);

        var tools = await db
            .Database.SqlQuery<ToolRow>(
                $"""
                SELECT tool->>'Name' AS "Name", tool->>'Status' AS "Status", count(*)::int AS "Count"
                FROM assistant_turns, jsonb_array_elements("Tools") AS tool
                WHERE "StartedAt" > {from} AND "StartedAt" <= {to}
                GROUP BY 1, 2 ORDER BY 3 DESC
                """
            )
            .ToListAsync(ct);

        // Token totals only over turns that reported them; the rest are counted, not treated as zero.
        var withTokens = turns.Where(t => t.InputTokens != null);
        var tokenTurns = await withTokens.CountAsync(ct);
        long? input =
            tokenTurns == 0 ? null : await withTokens.SumAsync(t => (long)t.InputTokens!.Value, ct);
        long? output =
            tokenTurns == 0
                ? null
                : await withTokens.SumAsync(t => (long)(t.OutputTokens ?? 0), ct);
        var total = outcomes.Sum(o => o.Count);

        var security = await db
            .SecurityEvents.Where(e => e.OccurredAt > from && e.OccurredAt <= to)
            .GroupBy(e => e.Kind)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return TypedResults.Ok(
            new ObservabilitySummary(
                from,
                to,
                new ChatMetrics(
                    total,
                    Outcome(TurnOutcome.Done),
                    Outcome(TurnOutcome.Error),
                    Outcome(TurnOutcome.Cancelled),
                    Round(percentiles.First),
                    Round(percentiles.Total),
                    errorCodes,
                    tools.Select(t => new ToolCount(t.Name, t.Status, t.Count)).ToList(),
                    input,
                    output,
                    total - tokenTurns
                ),
                security
                    .Select(s => new CountItem(s.Key.ToString(), s.Count))
                    .OrderByDescending(s => s.Count)
                    .ToList(),
                (await rules.EvaluateAsync(to, ct)).ToList()
            )
        );
    }

    private static double? Round(double? value) =>
        value is null ? null : Math.Round(value.Value, 1);

    /// <summary>
    /// Unreviewed turns that failed or had a rejected/failed tool, newest first; a pseudonym with
    /// repeated failures is marked. Abandoned turns are not listed: leaving is a signal, not a failure.
    /// </summary>
    public static async Task<Ok<ReviewQueue>> Queue(
        AppDbContext db,
        TimeProvider clock,
        IOptions<ObservabilityOptions> options,
        CancellationToken ct
    )
    {
        var settings = options.Value;
        var since = clock.GetUtcNow() - settings.TraceRetention;
        var suspicious = await db
            .AssistantTurns.AsNoTracking()
            .Where(t => t.ReviewedAt == null && t.StartedAt > since)
            // A tool cut off by the client leaving is not a failure (see Reasons and the doc comment).
            .Where(t =>
                t.Outcome == TurnOutcome.Error
                || (t.Outcome != TurnOutcome.Cancelled && t.Tools.Any(tool => tool.Status != "ok"))
            )
            .OrderByDescending(t => t.StartedAt)
            .ThenByDescending(t => t.Id)
            .Take(settings.ReviewQueueLimit)
            .ToListAsync(ct);

        var pseudonyms = suspicious.Select(t => t.UserPseudonym).Distinct().ToList();
        var repeated = await db
            .AssistantTurns.Where(t =>
                pseudonyms.Contains(t.UserPseudonym)
                && t.StartedAt > since
                && t.Outcome == TurnOutcome.Error
            )
            .GroupBy(t => t.UserPseudonym)
            .Where(g => g.Count() >= settings.RepeatedErrorsPerUser)
            .Select(g => g.Key)
            .ToListAsync(ct);

        return TypedResults.Ok(
            new ReviewQueue(
                suspicious
                    .Select(t => new ReviewItem(
                        t.Id,
                        t.CorrelationId,
                        t.UserPseudonym,
                        t.StartedAt,
                        t.Outcome.ToString(),
                        t.ErrorCode,
                        t.Provider,
                        t.Model,
                        t.PromptVersion,
                        t.TotalMs,
                        t.Tools.Select(x => new ReviewTool(x.Name, x.Status, x.DurationMs))
                            .ToList(),
                        Reasons(t, repeated.Contains(t.UserPseudonym))
                    ))
                    .ToList()
            )
        );
    }

    private static List<string> Reasons(AssistantTurn turn, bool repeated)
    {
        var reasons = new List<string>();
        if (turn.Outcome == TurnOutcome.Error)
            reasons.Add("error");
        if (turn.Tools.Any(t => t.Status == "rejected"))
            reasons.Add("rejected_tool");
        if (turn.Tools.Any(t => t.Status is not ("ok" or "rejected")))
            reasons.Add("tool_failure");
        if (repeated)
            reasons.Add("repeated_errors");
        return reasons;
    }

    public static async Task<Results<NoContent, ProblemHttpResult>> MarkReviewed(
        Guid id,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        TimeProvider clock,
        CancellationToken ct
    )
    {
        var reviewer = users.GetUserId(http.User)!;
        var now = clock.GetUtcNow();
        // Idempotent: marking a reviewed turn again keeps the first review.
        var found = await db.AssistantTurns.AnyAsync(t => t.Id == id, ct);
        if (!found)
            return ApiProblems.Problem(
                StatusCodes.Status404NotFound,
                "Turn not found",
                TurnNotFound
            );
        await db
            .AssistantTurns.Where(t => t.Id == id && t.ReviewedAt == null)
            .ExecuteUpdateAsync(
                set =>
                    set.SetProperty(t => t.ReviewedAt, now)
                        .SetProperty(t => t.ReviewedBy, reviewer),
                ct
            );
        return TypedResults.NoContent();
    }
}
