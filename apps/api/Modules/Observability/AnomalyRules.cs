using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Assistant;
using FuelClub.Api.Modules.Assistant.Tracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Observability;

public sealed record AnomalyResult(
    string Rule,
    string Severity,
    bool Enabled,
    double Value,
    double Threshold,
    int Sample,
    int MinSample,
    int WindowMinutes,
    bool Triggered,
    DateTimeOffset? LastAlertAt
);

/// <summary>
/// The configured rules over the last window. Counts come from security_events and assistant_turns;
/// a rate rule (AI errors) is judged only with at least MinSample turns.
/// </summary>
public sealed class AnomalyRules(
    AppDbContext db,
    IOptions<ObservabilityOptions> options,
    AnomalyAlerts alerts
)
{
    public const string AccessDenied = "access_denied";
    public const string LoginFailed = "login_failed";
    public const string IdempotencyConflict = "idempotency_conflict";
    public const string RepeatedRedemption = "repeated_redemption";
    public const string RateLimited = "rate_limited";
    public const string ToolLimit = "tool_limit";
    public const string RejectedTools = "rejected_tools";
    public const string AiErrors = "ai_errors";

    public static readonly IReadOnlyList<string> All =
    [
        AccessDenied,
        LoginFailed,
        IdempotencyConflict,
        RepeatedRedemption,
        RateLimited,
        ToolLimit,
        RejectedTools,
        AiErrors,
    ];

    private static readonly Dictionary<string, SecurityEventKind> SecurityKinds = new()
    {
        [AccessDenied] = SecurityEventKind.AccessDenied,
        [LoginFailed] = SecurityEventKind.LoginFailed,
        [IdempotencyConflict] = SecurityEventKind.IdempotencyConflict,
        [RepeatedRedemption] = SecurityEventKind.RepeatedRedemption,
        [RateLimited] = SecurityEventKind.RateLimited,
    };

    private AnomalyRuleOptions Rule(string name) =>
        options.Value.Rules.TryGetValue(name, out var rule)
            ? rule
            : ObservabilityOptions.DefaultRules()[name];

    public async Task<IReadOnlyList<AnomalyResult>> EvaluateAsync(
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var results = new List<AnomalyResult>();
        foreach (var name in All)
        {
            var rule = Rule(name);
            var since = now - rule.Window;
            double value;
            int sample;
            if (SecurityKinds.TryGetValue(name, out var kind))
            {
                sample = await db.SecurityEvents.CountAsync(
                    e => e.Kind == kind && e.OccurredAt > since && e.OccurredAt <= now,
                    ct
                );
                value = sample;
            }
            else if (name == ToolLimit)
            {
                sample = await Turns(since, now)
                    .CountAsync(t => t.ErrorCode == AssistantErrorCodes.ToolLimit, ct);
                value = sample;
            }
            else if (name == RejectedTools)
            {
                sample = await Turns(since, now)
                    .CountAsync(t => t.Tools.Any(tool => tool.Status == "rejected"), ct);
                value = sample;
            }
            else
            {
                // Share of failed turns among finished ones; a client leaving is not an AI error.
                var finished = Turns(since, now).Where(t => t.Outcome != TurnOutcome.Cancelled);
                sample = await finished.CountAsync(ct);
                var failed = await finished.CountAsync(
                    t =>
                        t.ErrorCode == AssistantErrorCodes.ProviderError
                        || t.ErrorCode == AssistantErrorCodes.Timeout,
                    ct
                );
                value = sample == 0 ? 0 : (double)failed / sample;
            }
            var judged = sample >= rule.MinSample;
            results.Add(
                new AnomalyResult(
                    name,
                    rule.Severity,
                    rule.Enabled,
                    Math.Round(value, 4),
                    rule.Threshold,
                    sample,
                    rule.MinSample,
                    (int)rule.Window.TotalMinutes,
                    rule.Enabled && judged && value >= rule.Threshold,
                    alerts.LastAlertAt(name)
                )
            );
        }
        return results;
    }

    private IQueryable<AssistantTurn> Turns(DateTimeOffset since, DateTimeOffset now) =>
        db.AssistantTurns.Where(t => t.StartedAt > since && t.StartedAt <= now);

    public TimeSpan Cooldown(string name) => Rule(name).Cooldown;
}

/// <summary>
/// Alerts for triggered rules: a structured warning (critical rules as error) in the log, at most once
/// per rule per cooldown. In memory: a restart may repeat an alert. Slack and other channels are not
/// configured in this demo.
/// </summary>
public sealed class AnomalyAlerts(ILogger<AnomalyAlerts> logger)
{
    private readonly Dictionary<string, DateTimeOffset> _last = new();
    private readonly Lock _gate = new();

    public DateTimeOffset? LastAlertAt(string rule)
    {
        lock (_gate)
            return _last.TryGetValue(rule, out var at) ? at : null;
    }

    /// <summary>Returns the rules that alerted now.</summary>
    public IReadOnlyList<string> Raise(
        IEnumerable<AnomalyResult> results,
        Func<string, TimeSpan> cooldown,
        DateTimeOffset now
    )
    {
        var raised = new List<string>();
        foreach (var result in results.Where(r => r.Triggered))
        {
            lock (_gate)
            {
                if (
                    _last.TryGetValue(result.Rule, out var last)
                    && now - last < cooldown(result.Rule)
                )
                    continue;
                _last[result.Rule] = now;
            }
            raised.Add(result.Rule);
            logger.Log(
                result.Severity == "critical" ? LogLevel.Error : LogLevel.Warning,
                "Anomaly {Rule}: {Value} >= {Threshold} in {WindowMinutes} min (sample {Sample})",
                result.Rule,
                result.Value,
                result.Threshold,
                result.WindowMinutes,
                result.Sample
            );
        }
        return raised;
    }
}
