using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;

namespace FuelClub.Api.Modules.Assistant.Conversations;

public sealed record ConversationResponse(
    Guid Id,
    DateTimeOffset StartedAt,
    DateTimeOffset? EndedAt,
    string? EndReason,
    int Turns
);

public static class ConversationEndpoints
{
    public const string RateLimitPolicy = "chat-conversations";

    public const string StartDescription =
        "Starts a chat conversation before its first turn (ADR 0017). With Idempotency-Key: a retry "
        + "after a lost response returns the same conversation, so no duplicate is created.";

    public const string EndDescription =
        "Ends the conversation (new chat, logout, end of a scenario). Idempotent: an ended conversation "
        + "keeps its first reason; one left idle ends as Idle. Ending never means the matter was solved. "
        + "404 assistant.conversation_not_found for an unknown id or someone else's.";

    public static async Task<Created<ConversationResponse>> Start(
        HttpContext http,
        UserManager<ApplicationUser> users,
        Conversations conversations,
        CancellationToken ct
    )
    {
        var conversation = await conversations.StartAsync(users.GetUserId(http.User)!, ct);
        return TypedResults.Created((string?)null, ToResponse(conversation));
    }

    public static async Task<Results<Ok<ConversationResponse>, ProblemHttpResult>> End(
        Guid id,
        HttpContext http,
        UserManager<ApplicationUser> users,
        Conversations conversations,
        CancellationToken ct
    )
    {
        var conversation = await conversations.EndAsync(id, users.GetUserId(http.User)!, ct);
        return conversation is null ? NotFound() : TypedResults.Ok(ToResponse(conversation));
    }

    public static ProblemHttpResult NotFound() =>
        ApiProblems.Problem(
            StatusCodes.Status404NotFound,
            "Conversation not found",
            AssistantErrorCodes.ConversationNotFound
        );

    public static ProblemHttpResult Ended() =>
        ApiProblems.Problem(
            StatusCodes.Status409Conflict,
            "Conversation has ended",
            AssistantErrorCodes.ConversationEnded
        );

    private static ConversationResponse ToResponse(AssistantConversation c) =>
        new(c.Id, c.StartedAt, c.EndedAt, c.EndReason?.ToString(), c.Turns);
}
