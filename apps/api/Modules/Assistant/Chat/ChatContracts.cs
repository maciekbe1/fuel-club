namespace FuelClub.Api.Modules.Assistant.Chat;

public sealed record ChatHistoryItem(string? Role, string? Text);

/// <summary>One turn. The model never gets the user's identity from here: it comes from the session.</summary>
public sealed record ChatRequest(string? Message, IReadOnlyList<ChatHistoryItem>? History);

/// <summary>Always first. <c>Demo</c> marks the rule-based adapter that needs no provider key.</summary>
public sealed record ChatMetaEvent(
    string CorrelationId,
    string Provider,
    string Model,
    string PromptVersion,
    bool Demo
);

/// <summary>A tool call happened: name and outcome only, never arguments or data.</summary>
public sealed record ChatToolEvent(string Name, string Status);

public sealed record ChatDeltaEvent(string Text);

/// <summary>
/// A proposed activation. Nothing changed yet: the user confirms with a button that posts
/// <c>Token</c> to /api/chat/confirmations. The coupon code is never part of the chat.
/// </summary>
public sealed record ChatConfirmationEvent(
    Guid ProposalId,
    Guid CouponId,
    string OfferName,
    decimal DiscountPerLiter,
    decimal MaxLiters,
    DateTimeOffset ExpiresAt,
    string Token
);

/// <summary>The turn ended successfully; sent only then.</summary>
public sealed record ChatDoneEvent(int ToolCalls);

/// <summary>The turn ended with an error; no <c>done</c> follows.</summary>
public sealed record ChatErrorEvent(string Code, string Message);

/// <summary>
/// Not a response body: documents the payload of every SSE event (event name = property name) so
/// the frontend gets generated types.
/// </summary>
public sealed record ChatStreamContract(
    ChatMetaEvent Meta,
    ChatToolEvent Tool,
    ChatDeltaEvent Delta,
    ChatConfirmationEvent Confirmation,
    ChatDoneEvent Done,
    ChatErrorEvent Error
);

public sealed record ConfirmationRequest(string? Token);
