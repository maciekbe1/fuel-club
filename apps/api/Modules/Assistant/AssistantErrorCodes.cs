namespace FuelClub.Api.Modules.Assistant;

public static class AssistantErrorCodes
{
    public const string ValidationFailed = "assistant.validation_failed";
    public const string RequiredField = "assistant.required_field";
    public const string TooLong = "assistant.too_long";
    public const string InvalidFormat = "assistant.invalid_format";
    public const string ConfirmationInvalid = "assistant.confirmation_invalid";
    public const string ConfirmationExpired = "assistant.confirmation_expired";
    public const string HandoffActive = "assistant.handoff_active";
    public const string ConversationNotFound = "assistant.conversation_not_found";
    public const string ConversationEnded = "assistant.conversation_ended";

    // Stream errors (the `error` event), not HTTP statuses.
    public const string Timeout = "assistant.timeout";
    public const string ToolLimit = "assistant.tool_limit";
    public const string ProviderError = "assistant.provider_error";
}

public sealed record AssistantValidationError(string Field, string Code)
    : FuelClub.Api.Infrastructure.IFieldError;
