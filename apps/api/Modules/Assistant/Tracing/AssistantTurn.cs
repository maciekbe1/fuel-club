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

    /// <summary>Model calls in the turn (failed ones too); null for turns recorded before P16c.</summary>
    public int? ModelSteps { get; set; }

    /// <summary>
    /// Estimated when the turn was recorded, from the price version next to it (ADR 0018). Null:
    /// not applicable (Demo) or unknown (no usage or no price) - never zero by default.
    /// </summary>
    public decimal? CostUsd { get; set; }
    public string? PriceVersion { get; set; }

    /// <summary>The conversation of this turn (ADR 0017); null for turns recorded before P16b.</summary>
    public Guid? ConversationId { get; set; }

    /// <summary>A consultant proposal reached the client in this turn (the handoff event).</summary>
    public bool HandoffProposed { get; set; }

    /// <summary>From the instance configuration (Simulation), never from a request.</summary>
    public bool Synthetic { get; set; }
    public string? RunId { get; set; }

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
