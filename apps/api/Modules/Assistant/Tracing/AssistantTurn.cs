namespace FuelClub.Api.Modules.Assistant.Tracing;

public enum TurnOutcome
{
    Done,
    Error,
    Cancelled,
}

/// <summary>
/// Metadata of one chat turn (the trace). Never the message, the answer, tool arguments or results:
/// what was asked stays with the user, the trace says only what the assistant did and how it ended.
/// </summary>
public sealed class AssistantTurn
{
    public Guid Id { get; set; }
    public required string CorrelationId { get; set; }

    /// <summary>HMAC of the user id (see TracePseudonyms); not reversible without the key.</summary>
    public required string UserPseudonym { get; set; }
    public required string Provider { get; set; }
    public required string Model { get; set; }
    public required string PromptVersion { get; set; }
    public bool Demo { get; set; }
    public DateTimeOffset StartedAt { get; set; }

    /// <summary>From the start of the turn to the first event after meta; null if none came.</summary>
    public int? FirstEventMs { get; set; }
    public int TotalMs { get; set; }
    public TurnOutcome Outcome { get; set; }
    public string? ErrorCode { get; set; }
    public int ToolCalls { get; set; }
    public List<TurnToolCall> Tools { get; set; } = [];

    /// <summary>Only what the provider reports; null means unknown, not zero.</summary>
    public int? InputTokens { get; set; }
    public int? OutputTokens { get; set; }

    /// <summary>Set when an admin marked the turn as reviewed in the review queue.</summary>
    public DateTimeOffset? ReviewedAt { get; set; }
    public string? ReviewedBy { get; set; }
}

/// <summary>A tool name from the closed list (or "unknown"), its status and duration.</summary>
public sealed class TurnToolCall
{
    public required string Name { get; set; }
    public required string Status { get; set; }
    public int DurationMs { get; set; }
}
