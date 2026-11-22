using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Observability;

/// <summary>One anomaly rule. Threshold is a count, or a share (0-1) for rate rules.</summary>
public sealed class AnomalyRuleOptions
{
    public bool Enabled { get; set; } = true;
    public double Threshold { get; set; }
    public TimeSpan Window { get; set; } = TimeSpan.FromMinutes(15);

    /// <summary>Rate rules: fewer events than this in the window are not judged.</summary>
    public int MinSample { get; set; } = 1;

    /// <summary>After an alert, the same rule does not alert again for this long.</summary>
    public TimeSpan Cooldown { get; set; } = TimeSpan.FromMinutes(30);
    public string Severity { get; set; } = "warning";
}

public sealed class ObservabilityOptions
{
    public const string Section = "Observability";

    public TimeSpan TraceRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan SecurityEventRetention { get; set; } = TimeSpan.FromDays(30);
    public TimeSpan PurgeInterval { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan MonitorInterval { get; set; } = TimeSpan.FromMinutes(1);

    /// <summary>Runs retention and alerts in the background; tests call them directly instead.</summary>
    public bool WorkerEnabled { get; set; } = true;

    /// <summary>A pseudonym with at least this many failed turns in the review window is flagged.</summary>
    public int RepeatedErrorsPerUser { get; set; } = 3;
    public int ReviewQueueLimit { get; set; } = 50;

    /// <summary>
    /// Security events stored per minute; above it they are dropped (and counted in the log), so a
    /// flood of rejected requests cannot turn into a flood of inserts. Far above every rule threshold.
    /// </summary>
    public int MaxSecurityEventsPerMinute { get; set; } = 600;

    public Dictionary<string, AnomalyRuleOptions> Rules { get; set; } = DefaultRules();

    public static Dictionary<string, AnomalyRuleOptions> DefaultRules() =>
        new(StringComparer.OrdinalIgnoreCase)
        {
            [AnomalyRules.AccessDenied] = new() { Threshold = 20 },
            [AnomalyRules.LoginFailed] = new() { Threshold = 20 },
            [AnomalyRules.IdempotencyConflict] = new() { Threshold = 5 },
            [AnomalyRules.RepeatedRedemption] = new() { Threshold = 3 },
            [AnomalyRules.RateLimited] = new() { Threshold = 20 },
            [AnomalyRules.ToolLimit] = new() { Threshold = 3 },
            [AnomalyRules.RejectedTools] = new() { Threshold = 5 },
            [AnomalyRules.AiErrors] = new()
            {
                Threshold = 0.2,
                MinSample = 10,
                Severity = "critical",
            },
        };
}

public sealed class ObservabilityOptionsValidator : IValidateOptions<ObservabilityOptions>
{
    public ValidateOptionsResult Validate(string? name, ObservabilityOptions o)
    {
        var errors = new List<string>();
        static bool Between(TimeSpan value, TimeSpan min, TimeSpan max) =>
            value >= min && value <= max;
        if (!Between(o.TraceRetention, TimeSpan.FromHours(1), TimeSpan.FromDays(365)))
            errors.Add("Observability:TraceRetention must be 1 h - 365 days.");
        if (!Between(o.SecurityEventRetention, TimeSpan.FromHours(1), TimeSpan.FromDays(365)))
            errors.Add("Observability:SecurityEventRetention must be 1 h - 365 days.");
        if (!Between(o.PurgeInterval, TimeSpan.FromSeconds(1), TimeSpan.FromDays(1)))
            errors.Add("Observability:PurgeInterval must be 1 s - 1 day.");
        if (!Between(o.MonitorInterval, TimeSpan.FromSeconds(1), TimeSpan.FromHours(1)))
            errors.Add("Observability:MonitorInterval must be 1 s - 1 h.");
        if (o.RepeatedErrorsPerUser is < 1 or > 1000)
            errors.Add("Observability:RepeatedErrorsPerUser must be 1-1000.");
        if (o.MaxSecurityEventsPerMinute is < 1 or > 100_000)
            errors.Add("Observability:MaxSecurityEventsPerMinute must be 1-100000.");
        if (o.ReviewQueueLimit is < 1 or > 500)
            errors.Add("Observability:ReviewQueueLimit must be 1-500.");
        foreach (var (rule, options) in o.Rules)
        {
            if (!AnomalyRules.All.Contains(rule, StringComparer.OrdinalIgnoreCase))
                errors.Add($"Observability:Rules:{rule} is not a known rule.");
            if (options.Threshold <= 0)
                errors.Add($"Observability:Rules:{rule}:Threshold must be > 0.");
            if (
                rule.Equals(AnomalyRules.AiErrors, StringComparison.OrdinalIgnoreCase)
                && options.Threshold > 1
            )
                errors.Add($"Observability:Rules:{rule}:Threshold is a share (0-1].");
            if (!Between(options.Window, TimeSpan.FromMinutes(1), TimeSpan.FromDays(7)))
                errors.Add($"Observability:Rules:{rule}:Window must be 1 min - 7 days.");
            if (options.MinSample < 1)
                errors.Add($"Observability:Rules:{rule}:MinSample must be >= 1.");
            if (!Between(options.Cooldown, TimeSpan.Zero, TimeSpan.FromDays(1)))
                errors.Add($"Observability:Rules:{rule}:Cooldown must be 0 - 1 day.");
            if (options.Severity is not ("warning" or "critical"))
                errors.Add($"Observability:Rules:{rule}:Severity must be warning or critical.");
        }
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}
