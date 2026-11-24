using System.Globalization;
using System.Text;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Assistant.Conversations;
using FuelClub.Api.Modules.Assistant.Tracing;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.WebUtilities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NpgsqlTypes;

namespace FuelClub.Api.Modules.Observability;

/// <summary>A conversation in the admin list (P15c): metadata and pseudonyms, never the text.</summary>
public sealed record ConversationListItem(
    Guid Id,
    string UserPseudonym,
    DateTimeOffset StartedAt,
    DateTimeOffset LastActivityAt,
    DateTimeOffset? EndedAt,
    string? EndReason,
    int Turns,
    bool HandoffProposed,
    bool HasCase,
    bool Synthetic,
    string? RunId,
    List<string> Models,
    List<string> PromptVersions,
    List<string> Reasons
);

public sealed record ConversationPage(List<ConversationListItem> Items, string? NextCursor);

/// <summary>One turn of a conversation as a trace: timing, outcome, model steps and tools.</summary>
public sealed record TurnTrace(
    Guid Id,
    string CorrelationId,
    DateTimeOffset StartedAt,
    int? FirstEventMs,
    int TotalMs,
    string Outcome,
    string? ErrorCode,
    string Provider,
    string Model,
    string PromptVersion,
    int? ModelSteps,
    int? InputTokens,
    int? OutputTokens,
    decimal? CostUsd,
    string? PriceVersion,
    bool HandoffProposed,
    DateTimeOffset? ReviewedAt,
    List<ReviewTool> Tools,
    List<string> Reasons
);

public sealed record ConversationTrace(ConversationListItem Conversation, List<TurnTrace> Turns);

/// <summary>
/// Conversations and their turns for the admin (P15c, ADR 0020). Metadata only, as the turns
/// (ADR 0008): no message text, arguments or results, users as pseudonyms, the support case only
/// as "has a case" - its content stays with Support.
/// </summary>
public static class ConversationTraces
{
    public const int DefaultLimit = 25;
    public const int MaxLimit = 100;
    public const string ConversationNotFound = "observability.conversation_not_found";
    public const string InvalidCursor = "observability.invalid_cursor";
    private const string CursorPrefix = "c1:";

    private sealed record IdRow(Guid Id, DateTimeOffset StartedAt);

    // A failed turn, or a tool that did not succeed in a turn the client did not leave. The same
    // rule as the review queue; leaving is a signal, not a failure.
    private const string SuspiciousTurn = """
        (t."Outcome" = 'Error'
         OR (t."Outcome" <> 'Cancelled' AND EXISTS (
             SELECT 1 FROM jsonb_array_elements(t."Tools") AS tool WHERE tool->>'Status' <> 'ok')))
        """;

    public static async Task<Results<Ok<ConversationPage>, ProblemHttpResult>> List(
        AppDbContext db,
        TimeProvider clock,
        IOptions<ObservabilityOptions> options,
        int? hours,
        string? traffic,
        string? runId,
        string? model,
        string? promptVersion,
        bool? suspicious,
        int? limit,
        string? cursor,
        CancellationToken ct
    )
    {
        var span = hours ?? 24;
        var mode = traffic ?? ObservabilityEndpoints.Real;
        var size = limit ?? DefaultLimit;
        var errors = new List<ObservabilityValidationError>();
        if (span is < 1 or > 24 * 30)
            errors.Add(new("hours", ObservabilityEndpoints.OutOfRange));
        if (
            mode
            is not (
                ObservabilityEndpoints.Real
                or ObservabilityEndpoints.SyntheticTraffic
                or ObservabilityEndpoints.All
            )
        )
            errors.Add(new("traffic", ObservabilityEndpoints.OutOfRange));
        if (size is < 1 or > MaxLimit)
            errors.Add(new("limit", ObservabilityEndpoints.OutOfRange));
        foreach (
            var (field, value) in new[]
            {
                ("runId", runId),
                ("model", model),
                ("promptVersion", promptVersion),
            }
        )
            if (value is { Length: 0 or > 100 })
                errors.Add(new(field, ObservabilityEndpoints.OutOfRange));
        (DateTimeOffset To, DateTimeOffset At, Guid Id)? after = null;
        if (cursor is not null)
        {
            if (TryDecode(cursor, out var position))
                after = position;
            else
                errors.Add(new("cursor", InvalidCursor));
        }
        if (errors.Count > 0)
            return ApiProblems.Validation(
                "Filter out of range",
                ObservabilityEndpoints.ValidationFailed,
                errors
            );

        // The window end of the first page travels in the cursor: later pages use the same
        // window, so its old edge does not move forward between clicks.
        var to = after?.To ?? clock.GetUtcNow();
        var from = to - TimeSpan.FromHours(span);
        // Keyset on (StartedAt, Id), newest first: a page never repeats or skips a row that
        // existed when the first page was read.
        var rows = await db
            .Database.SqlQueryRaw<IdRow>(
                $"""
                SELECT c."Id", c."StartedAt"
                FROM assistant_conversations c
                WHERE c."StartedAt" > @from AND c."StartedAt" <= @to
                  AND (@traffic = 'all' OR c."Synthetic" = (@traffic = 'synthetic'))
                  AND (@run IS NULL OR c."RunId" = @run)
                  AND ((@model IS NULL AND @prompt IS NULL) OR EXISTS (SELECT 1 FROM assistant_turns t
                       WHERE t."ConversationId" = c."Id"
                         AND (@model IS NULL OR t."Model" = @model)
                         AND (@prompt IS NULL OR t."PromptVersion" = @prompt)))
                  AND (NOT @suspicious OR EXISTS (SELECT 1 FROM assistant_turns t
                       WHERE t."ConversationId" = c."Id" AND {SuspiciousTurn}))
                  AND (@afterAt IS NULL OR (c."StartedAt", c."Id") < (@afterAt, @afterId))
                ORDER BY c."StartedAt" DESC, c."Id" DESC
                LIMIT @take
                """,
                new NpgsqlParameter("from", from),
                new NpgsqlParameter("to", to),
                new NpgsqlParameter("traffic", NpgsqlDbType.Text) { Value = mode },
                new NpgsqlParameter("run", NpgsqlDbType.Text)
                {
                    Value = (object?)runId ?? DBNull.Value,
                },
                new NpgsqlParameter("model", NpgsqlDbType.Text)
                {
                    Value = (object?)model ?? DBNull.Value,
                },
                new NpgsqlParameter("prompt", NpgsqlDbType.Text)
                {
                    Value = (object?)promptVersion ?? DBNull.Value,
                },
                new NpgsqlParameter("suspicious", suspicious ?? false),
                new NpgsqlParameter("afterAt", NpgsqlDbType.TimestampTz)
                {
                    Value = (object?)after?.At ?? DBNull.Value,
                },
                new NpgsqlParameter("afterId", NpgsqlDbType.Uuid)
                {
                    Value = (object?)after?.Id ?? Guid.Empty,
                },
                // One extra row tells whether another page exists.
                new NpgsqlParameter("take", size + 1)
            )
            .ToListAsync(ct);

        var page = rows.Take(size).ToList();
        var (items, _, _) = await LoadAsync(
            db,
            options.Value,
            clock.GetUtcNow(),
            page.Select(r => r.Id).ToList(),
            ct
        );
        var next = rows.Count > size ? Encode(to, page[^1].StartedAt, page[^1].Id) : null;
        return TypedResults.Ok(new ConversationPage(items, next));
    }

    public static async Task<Results<Ok<ConversationTrace>, ProblemHttpResult>> Get(
        Guid id,
        AppDbContext db,
        TimeProvider clock,
        IOptions<ObservabilityOptions> options,
        CancellationToken ct
    )
    {
        var (items, byConversation, repeated) = await LoadAsync(
            db,
            options.Value,
            clock.GetUtcNow(),
            [id],
            ct
        );
        if (items is not [var conversation])
            return ApiProblems.Problem(
                StatusCodes.Status404NotFound,
                "Conversation not found",
                ConversationNotFound
            );
        var turns = byConversation[id].OrderBy(t => t.StartedAt).ThenBy(t => t.Id).ToList();
        return TypedResults.Ok(
            new ConversationTrace(
                conversation,
                turns
                    .Select(t => new TurnTrace(
                        t.Id,
                        t.CorrelationId,
                        t.StartedAt,
                        t.FirstEventMs,
                        t.TotalMs,
                        t.Outcome.ToString(),
                        t.ErrorCode,
                        t.Provider,
                        t.Model,
                        t.PromptVersion,
                        t.ModelSteps,
                        t.InputTokens,
                        t.OutputTokens,
                        t.CostUsd,
                        t.PriceVersion,
                        t.HandoffProposed,
                        t.ReviewedAt,
                        t.Tools.Select(x => new ReviewTool(x.Name, x.Status, x.DurationMs))
                            .ToList(),
                        Reasons(t, repeated)
                    ))
                    .ToList()
            )
        );
    }

    /// <summary>
    /// The queue's reasons, only for a suspicious turn: one the client left is not a failure, and
    /// "repeated errors" marks a failing turn, not every turn of that user.
    /// </summary>
    private static List<string> Reasons(AssistantTurn turn, HashSet<string> repeated) =>
        turn.Outcome == TurnOutcome.Error
        || (turn.Outcome != TurnOutcome.Cancelled && turn.Tools.Any(t => t.Status != "ok"))
            ? ObservabilityEndpoints.Reasons(turn, repeated.Contains(turn.UserPseudonym))
            : [];

    /// <summary>
    /// The list rows in the order of <paramref name="ids"/> with flags from their turns, the turns
    /// by conversation and the pseudonyms with repeated errors - one query each.
    /// </summary>
    private static async Task<(
        List<ConversationListItem> Items,
        ILookup<Guid, AssistantTurn> Turns,
        HashSet<string> Repeated
    )> LoadAsync(
        AppDbContext db,
        ObservabilityOptions options,
        DateTimeOffset now,
        List<Guid> ids,
        CancellationToken ct
    )
    {
        if (ids.Count == 0)
            return ([], Array.Empty<AssistantTurn>().ToLookup(t => Guid.Empty), []);
        var conversations = await db
            .AssistantConversations.AsNoTracking()
            .Where(c => ids.Contains(c.Id))
            .ToDictionaryAsync(c => c.Id, ct);
        var turns = await db
            .AssistantTurns.AsNoTracking()
            .Where(t => t.ConversationId != null && ids.Contains(t.ConversationId.Value))
            .ToListAsync(ct);
        var repeated = await RepeatedAsync(db, options, now, turns, ct);
        var byConversation = turns.ToLookup(t => t.ConversationId!.Value);
        var items = ids.Where(conversations.ContainsKey)
            .Select(id =>
            {
                var c = conversations[id];
                var own = byConversation[id].ToList();
                return new ConversationListItem(
                    c.Id,
                    c.UserPseudonym,
                    c.StartedAt,
                    c.LastActivityAt,
                    c.EndedAt,
                    c.EndReason?.ToString(),
                    c.Turns,
                    own.Any(t => t.HandoffProposed),
                    c.SupportCaseId is not null,
                    c.Synthetic,
                    c.RunId,
                    own.Select(t => t.Model).Distinct().Order(StringComparer.Ordinal).ToList(),
                    own.Select(t => t.PromptVersion)
                        .Distinct()
                        .Order(StringComparer.Ordinal)
                        .ToList(),
                    own.SelectMany(t => Reasons(t, repeated)).Distinct().ToList()
                );
            })
            .ToList();
        return (items, byConversation, repeated);
    }

    /// <summary>
    /// Pseudonyms with repeated failed turns within the trace retention (the review queue's rule).
    /// </summary>
    private static async Task<HashSet<string>> RepeatedAsync(
        AppDbContext db,
        ObservabilityOptions options,
        DateTimeOffset now,
        IEnumerable<AssistantTurn> turns,
        CancellationToken ct
    )
    {
        var pseudonyms = turns.Select(t => t.UserPseudonym).Distinct().ToList();
        if (pseudonyms.Count == 0)
            return [];
        var since = now - options.TraceRetention;
        var repeated = await db
            .AssistantTurns.Where(t =>
                pseudonyms.Contains(t.UserPseudonym)
                && t.StartedAt > since
                && t.Outcome == TurnOutcome.Error
            )
            .GroupBy(t => t.UserPseudonym)
            .Where(g => g.Count() >= options.RepeatedErrorsPerUser)
            .Select(g => g.Key)
            .ToListAsync(ct);
        return repeated.ToHashSet();
    }

    public static string Encode(DateTimeOffset to, DateTimeOffset at, Guid id) =>
        WebEncoders.Base64UrlEncode(
            Encoding.UTF8.GetBytes(
                $"{CursorPrefix}{to.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{at.UtcTicks.ToString(CultureInfo.InvariantCulture)}:{id:N}"
            )
        );

    private static bool TryDecode(
        string cursor,
        out (DateTimeOffset To, DateTimeOffset At, Guid Id) position
    )
    {
        position = default;
        try
        {
            var text = Encoding.UTF8.GetString(WebEncoders.Base64UrlDecode(cursor));
            if (!text.StartsWith(CursorPrefix, StringComparison.Ordinal))
                return false;
            var parts = text[CursorPrefix.Length..].Split(':');
            if (
                parts.Length != 3
                || !TryTicks(parts[0], out var to)
                || !TryTicks(parts[1], out var at)
                || !Guid.TryParseExact(parts[2], "N", out var id)
            )
                return false;
            position = (to, at, id);
            return true;
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool TryTicks(string text, out DateTimeOffset value)
    {
        value = default;
        if (
            !long.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var ticks)
            || ticks < DateTimeOffset.MinValue.UtcTicks
            || ticks > DateTimeOffset.MaxValue.UtcTicks
        )
            return false;
        value = new DateTimeOffset(ticks, TimeSpan.Zero);
        return true;
    }
}
