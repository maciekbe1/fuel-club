using System.Diagnostics;

namespace FuelClub.Api.Modules.Assistant.Tracing;

/// <summary>
/// The spans of a chat turn. Only the attributes listed here are ever set, and every value is
/// produced by the server (ids, enums, counts, names from the closed tool list): no message text,
/// arguments, results, codes or cookies, so nothing needs to be scrubbed before export.
/// </summary>
public static class AssistantTelemetry
{
    public const string SourceName = "FuelClub.Assistant";
    public static readonly ActivitySource Source = new(SourceName);

    public const string TurnSpan = "chat.turn";
    public const string ToolSpan = "chat.tool";

    public static class Attributes
    {
        public const string CorrelationId = "fuelclub.correlation_id";
        public const string UserPseudonym = "fuelclub.user_pseudonym";
        public const string Provider = "gen_ai.system";
        public const string Model = "gen_ai.request.model";
        public const string PromptVersion = "fuelclub.prompt_version";
        public const string Demo = "fuelclub.demo";
        public const string Outcome = "fuelclub.outcome";
        public const string ErrorType = "error.type";
        public const string ToolCalls = "fuelclub.tool_calls";
        public const string FirstEventMs = "fuelclub.first_event_ms";
        public const string InputTokens = "gen_ai.usage.input_tokens";
        public const string OutputTokens = "gen_ai.usage.output_tokens";
        public const string ToolName = "gen_ai.tool.name";
        public const string ToolStatus = "fuelclub.tool_status";

        public static readonly IReadOnlySet<string> All = new HashSet<string>
        {
            CorrelationId,
            UserPseudonym,
            Provider,
            Model,
            PromptVersion,
            Demo,
            Outcome,
            ErrorType,
            ToolCalls,
            FirstEventMs,
            InputTokens,
            OutputTokens,
            ToolName,
            ToolStatus,
        };
    }
}
