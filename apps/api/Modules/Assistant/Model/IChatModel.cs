using FuelClub.Api.Modules.Assistant.Chat;
using FuelClub.Api.Modules.Assistant.Tools;

namespace FuelClub.Api.Modules.Assistant.Model;

/// <summary>What the model sees: the turn and the results of tools it asked for in this turn.</summary>
public sealed record ChatModelContext(
    string Message,
    IReadOnlyList<ChatHistoryItem> History,
    IReadOnlyList<ToolResult> ToolResults
);

/// <summary>Tokens as reported by the provider for one model call.</summary>
public sealed record TokenUsage(int InputTokens, int OutputTokens);

/// <summary>
/// The model's next step: call a tool, or answer and end the turn. Usage is null when the provider
/// does not report it (the demo model never does).
/// </summary>
public abstract record ModelStep
{
    public TokenUsage? Usage { get; init; }

    public sealed record Tool(ToolCall Call) : ModelStep;

    public sealed record Reply(string Text) : ModelStep;
}

/// <summary>
/// A chat model behind the assistant. ChatService owns the loop: limits, tool execution and events.
/// A provider adapter (e.g. Gemini function calling) implements the same contract.
/// </summary>
public interface IChatModel
{
    string Provider { get; }
    string Model { get; }
    string PromptVersion { get; }
    bool Demo { get; }

    Task<ModelStep> NextAsync(ChatModelContext context, CancellationToken cancellationToken);
}
