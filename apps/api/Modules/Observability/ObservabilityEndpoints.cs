using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant.Tracing;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace FuelClub.Api.Modules.Observability;

public sealed record CountItem(string Key, int Count);

public sealed record ToolCount(string Name, string Status, int Count);

/// <summary>Turns in the window (ADR 0018). Tool calls are their own counter, not turns.</summary>
public sealed record TurnMetrics(
    int Turns,
    int Done,
    int Errors,
    int Cancelled,
    double? P50FirstEventMs,
    double? P95FirstEventMs,
    double? P50TotalMs,
    double? P95TotalMs,
    long ModelSteps,
    int TurnsWithStepData,
    double? P95ModelSteps,
    long ToolCalls,
    long ToolCallsNotOk,
    List<CountItem> ErrorCodes,
    List<ToolCount> Tools,
    long? InputTokens,
    long? OutputTokens,
    int TurnsWithoutTokenData,
    int DemoTurns
);

/// <summary>
/// Estimated, not a provider invoice. Demo turns: not applicable. Others without usage or a price:
/// unknown, never zero - <c>Usd</c> sums only the known ones, so it is complete only with no unknown.
/// </summary>
public sealed record CostMetrics(
    decimal? Usd,
    int KnownTurns,
    int UnknownTurns,
    int NotApplicableTurns,
    List<string> PriceVersions
);

/// <summary>
/// Conversations started in the window. Ended = Client + Idle + Handoff; an idle one past the
/// limit counts as Idle even before the server closed it. Handoff once per conversation: a case
/// linked to it. No end reason means the matter was solved (ADR 0017).
/// </summary>
public sealed record ConversationMetrics(
    int Started,
    int Open,
    int Ended,
    int EndedByClient,
    int EndedIdle,
    int EndedByHandoff,
    int WithHandoffProposal,
    int WithCase,
    int EndedWithoutCase
);

public sealed record SummaryFilters(
    string Traffic,
    string? RunId,
    string? Model,
    string? PromptVersion
);

/// <summary>Values present in the window for the chosen traffic, for the filter lists.</summary>
public sealed record FilterOptions(
    List<string> RunIds,
    List<string> Models,
    List<string> PromptVersions
);

public sealed record ObservabilitySummary(
    DateTimeOffset From,
    DateTimeOffset To,
    SummaryFilters Filters,
    FilterOptions Options,
    TurnMetrics Turns,
    CostMetrics Cost,
    ConversationMetrics Conversations,
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

    public const string Real = "real";
    public const string SyntheticTraffic = "synthetic";
    public const string All = "all";
    private const int FilterMaxLength = 100;

    private sealed record TurnRow(
        int Turns,
        int Done,
        int Errors,
        int Cancelled,
        double? P50First,
        double? P95First,
        double? P50Total,
        double? P95Total,
        long? Steps,
        int StepTurns,
        double? P95Steps,
        long? ToolCalls,
        int Demo,
        int CostKnown,
        int CostUnknown,
        decimal? Cost,
        int TokenTurns,
        int NonDemoWithoutTokens,
        long? Input,
        long? Output
    );

    private sealed record ToolRow(string Name, string Status, int Count);

    private sealed record CodeRow(string Key, int Count);

    private sealed record ValueRow(string Kind, string Value);

    private sealed record ConversationRow(
        int Started,
        int Open,
        int Client,
        int Idle,
        int Handoff,
        int WithProposal,
        int WithCase,
        int EndedWithoutCase
    );

    // One filter for every turn query: window, traffic, run, model, prompt version.
    private const string TurnFilter = """
        t."StartedAt" > @from AND t."StartedAt" <= @to
        AND (@traffic = 'all' OR t."Synthetic" = (@traffic = 'synthetic'))
        AND (@run IS NULL OR t."RunId" = @run)
        AND (@model IS NULL OR t."Model" = @model)
        AND (@prompt IS NULL OR t."PromptVersion" = @prompt)
        """;

    public static async Task<Results<Ok<ObservabilitySummary>, ProblemHttpResult>> Summary(
        AppDbContext db,
        AnomalyRules rules,
        TimeProvider clock,
        IOptions<Assistant.AssistantOptions> assistant,
        int? hours,
        string? traffic,
        string? runId,
        string? model,
        string? promptVersion,
        CancellationToken ct
    )
    {
        var span = hours ?? 24;
        var mode = traffic ?? Real;
        var errors = new List<ObservabilityValidationError>();
        if (span is < 1 or > 24 * 30)
            errors.Add(new("hours", OutOfRange));
        if (mode is not (Real or SyntheticTraffic or All))
            errors.Add(new("traffic", OutOfRange));
        foreach (
            var (field, value) in new[]
            {
                ("runId", runId),
                ("model", model),
                ("promptVersion", promptVersion),
            }
        )
            if (value is { Length: 0 or > FilterMaxLength })
                errors.Add(new(field, OutOfRange));
        if (errors.Count > 0)
            return ApiProblems.Validation("Filter out of range", ValidationFailed, errors);

        var to = clock.GetUtcNow();
        var from = to - TimeSpan.FromHours(span);
        NpgsqlParameter[] Parameters() =>
            [
                new("from", from),
                new("to", to),
                new("traffic", NpgsqlDbType.Text) { Value = mode },
                new("run", NpgsqlDbType.Text) { Value = (object?)runId ?? DBNull.Value },
                new("model", NpgsqlDbType.Text) { Value = (object?)model ?? DBNull.Value },
                new("prompt", NpgsqlDbType.Text) { Value = (object?)promptVersion ?? DBNull.Value },
                new("cutoff", to - assistant.Value.ConversationIdleTimeout),
            ];

        var row = await db
            .Database.SqlQueryRaw<TurnRow>(
                $"""
                SELECT count(*)::int AS "Turns",
                       count(*) FILTER (WHERE t."Outcome" = 'Done')::int AS "Done",
                       count(*) FILTER (WHERE t."Outcome" = 'Error')::int AS "Errors",
                       count(*) FILTER (WHERE t."Outcome" = 'Cancelled')::int AS "Cancelled",
                       percentile_cont(0.5) WITHIN GROUP (ORDER BY t."FirstEventMs") AS "P50First",
                       percentile_cont(0.95) WITHIN GROUP (ORDER BY t."FirstEventMs") AS "P95First",
                       percentile_cont(0.5) WITHIN GROUP (ORDER BY t."TotalMs") AS "P50Total",
                       percentile_cont(0.95) WITHIN GROUP (ORDER BY t."TotalMs") AS "P95Total",
                       sum(t."ModelSteps")::bigint AS "Steps",
                       count(t."ModelSteps")::int AS "StepTurns",
                       percentile_cont(0.95) WITHIN GROUP (ORDER BY t."ModelSteps") AS "P95Steps",
                       sum(jsonb_array_length(t."Tools"))::bigint AS "ToolCalls",
                       count(*) FILTER (WHERE t."Demo")::int AS "Demo",
                       count(*) FILTER (WHERE NOT t."Demo" AND t."CostUsd" IS NOT NULL)::int AS "CostKnown",
                       count(*) FILTER (WHERE NOT t."Demo" AND t."CostUsd" IS NULL)::int AS "CostUnknown",
                       sum(t."CostUsd") FILTER (WHERE NOT t."Demo") AS "Cost",
                       count(*) FILTER (WHERE t."InputTokens" IS NOT NULL)::int AS "TokenTurns",
                       count(*) FILTER (WHERE NOT t."Demo" AND t."InputTokens" IS NULL AND t."ModelSteps" IS DISTINCT FROM 0)::int AS "NonDemoWithoutTokens",
                       sum(t."InputTokens")::bigint AS "Input",
                       sum(t."OutputTokens")::bigint AS "Output"
                FROM assistant_turns t WHERE {TurnFilter}
                """,
                Parameters()
            )
            .SingleAsync(ct);

        var tools = await db
            .Database.SqlQueryRaw<ToolRow>(
                $"""
                SELECT tool->>'Name' AS "Name", tool->>'Status' AS "Status", count(*)::int AS "Count"
                FROM assistant_turns t, jsonb_array_elements(t."Tools") AS tool
                WHERE {TurnFilter}
                GROUP BY 1, 2 ORDER BY 3 DESC, 1, 2
                """,
                Parameters()
            )
            .ToListAsync(ct);

        var codes = await db
            .Database.SqlQueryRaw<CodeRow>(
                $"""
                SELECT t."ErrorCode" AS "Key", count(*)::int AS "Count"
                FROM assistant_turns t WHERE {TurnFilter} AND t."ErrorCode" IS NOT NULL
                GROUP BY 1 ORDER BY 2 DESC, 1
                """,
                Parameters()
            )
            .ToListAsync(ct);

        var prices = await db
            .Database.SqlQueryRaw<string>(
                $"""
                SELECT DISTINCT t."PriceVersion" AS "Value" FROM assistant_turns t
                WHERE {TurnFilter} AND t."PriceVersion" IS NOT NULL ORDER BY 1
                """,
                Parameters()
            )
            .ToListAsync(ct);

        // Only the window and the traffic: the lists offer what the other filters can pick.
        var values = await db
            .Database.SqlQueryRaw<ValueRow>(
                """
                SELECT DISTINCT 'run' AS "Kind", t."RunId" AS "Value" FROM assistant_turns t
                WHERE t."StartedAt" > @from AND t."StartedAt" <= @to AND t."RunId" IS NOT NULL
                  AND (@traffic = 'all' OR t."Synthetic" = (@traffic = 'synthetic'))
                UNION SELECT DISTINCT 'model', t."Model" FROM assistant_turns t
                WHERE t."StartedAt" > @from AND t."StartedAt" <= @to
                  AND (@traffic = 'all' OR t."Synthetic" = (@traffic = 'synthetic'))
                UNION SELECT DISTINCT 'prompt', t."PromptVersion" FROM assistant_turns t
                WHERE t."StartedAt" > @from AND t."StartedAt" <= @to
                  AND (@traffic = 'all' OR t."Synthetic" = (@traffic = 'synthetic'))
                """,
                Parameters()
            )
            .ToListAsync(ct);
        List<string> Values(string kind) =>
            values
                .Where(v => v.Kind == kind)
                .Select(v => v.Value)
                .Order(StringComparer.Ordinal)
                .ToList();

        // A conversation matches the model or prompt filter when one of its turns does.
        var conversations = await db
            .Database.SqlQueryRaw<ConversationRow>(
                """
                SELECT count(*)::int AS "Started",
                       count(*) FILTER (WHERE c."EndedAt" IS NULL AND c."LastActivityAt" >= @cutoff)::int AS "Open",
                       count(*) FILTER (WHERE c."EndReason" = 'Client')::int AS "Client",
                       count(*) FILTER (WHERE c."EndReason" = 'Idle'
                                        OR (c."EndedAt" IS NULL AND c."LastActivityAt" < @cutoff))::int AS "Idle",
                       count(*) FILTER (WHERE c."EndReason" = 'Handoff')::int AS "Handoff",
                       count(*) FILTER (WHERE EXISTS (SELECT 1 FROM assistant_turns p
                                        WHERE p."ConversationId" = c."Id" AND p."HandoffProposed"))::int AS "WithProposal",
                       count(*) FILTER (WHERE c."SupportCaseId" IS NOT NULL)::int AS "WithCase",
                       count(*) FILTER (WHERE c."SupportCaseId" IS NULL
                                        AND (c."EndedAt" IS NOT NULL OR c."LastActivityAt" < @cutoff))::int AS "EndedWithoutCase"
                FROM assistant_conversations c
                WHERE c."StartedAt" > @from AND c."StartedAt" <= @to
                  AND (@traffic = 'all' OR c."Synthetic" = (@traffic = 'synthetic'))
                  AND (@run IS NULL OR c."RunId" = @run)
                  AND (@model IS NULL OR EXISTS (SELECT 1 FROM assistant_turns m
                       WHERE m."ConversationId" = c."Id" AND m."Model" = @model))
                  AND (@prompt IS NULL OR EXISTS (SELECT 1 FROM assistant_turns m
                       WHERE m."ConversationId" = c."Id" AND m."PromptVersion" = @prompt))
                """,
                Parameters()
            )
            .SingleAsync(ct);

        var security = await db
            .SecurityEvents.Where(e => e.OccurredAt > from && e.OccurredAt <= to)
            .GroupBy(e => e.Kind)
            .Select(g => new { g.Key, Count = g.Count() })
            .ToListAsync(ct);

        return TypedResults.Ok(
            new ObservabilitySummary(
                from,
                to,
                new SummaryFilters(mode, runId, model, promptVersion),
                new FilterOptions(Values("run"), Values("model"), Values("prompt")),
                new TurnMetrics(
                    row.Turns,
                    row.Done,
                    row.Errors,
                    row.Cancelled,
                    Round(row.P50First),
                    Round(row.P95First),
                    Round(row.P50Total),
                    Round(row.P95Total),
                    row.Steps ?? 0,
                    row.StepTurns,
                    Round(row.P95Steps),
                    row.ToolCalls ?? 0,
                    tools.Where(t => t.Status != "ok").Sum(t => (long)t.Count),
                    codes.Select(c => new CountItem(c.Key, c.Count)).ToList(),
                    tools.Select(t => new ToolCount(t.Name, t.Status, t.Count)).ToList(),
                    row.TokenTurns == 0 ? null : row.Input,
                    row.TokenTurns == 0 ? null : row.Output ?? 0,
                    row.NonDemoWithoutTokens,
                    row.Demo
                ),
                new CostMetrics(
                    row.CostKnown == 0 ? null : row.Cost,
                    row.CostKnown,
                    row.CostUnknown,
                    row.Demo,
                    prices
                ),
                new ConversationMetrics(
                    conversations.Started,
                    conversations.Open,
                    conversations.Started - conversations.Open,
                    conversations.Client,
                    conversations.Idle,
                    conversations.Handoff,
                    conversations.WithProposal,
                    conversations.WithCase,
                    conversations.EndedWithoutCase
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
