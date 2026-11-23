using System.Text.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant.Confirmation;
using FuelClub.Api.Modules.Assistant.Tools;
using FuelClub.Api.Modules.Loyalty;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using FuelClub.Api.Modules.Rewards.Coupons;
using FuelClub.Api.Modules.Support;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Assistant.Chat;

public static class ChatEndpoints
{
    public const string RateLimitPolicy = "assistant-chat";

    public const string ChatDescription =
        "One chat turn as a server-sent event stream (POST, read with fetch). Events: meta (first), tool, "
        + "delta, confirmation, then exactly one of done or error; payloads are in ChatStreamContract. "
        + "Comment lines (': ping') are heartbeats. conversationId (from POST /api/chat/conversations) is "
        + "required. Validation errors, 429, 404 assistant.conversation_not_found (unknown or someone "
        + "else's), 409 assistant.conversation_ended (ended or idle: start a new one) and 409 "
        + "assistant.handoff_active (an open support case) come as ProblemDetails before the stream starts.";

    public const string ConfirmDescription =
        "Confirms an activation proposed in the chat (the token from a confirmation event) and activates the "
        + "coupon with the same logic as /api/me/coupons/{id}/activate. 404 for a token that is invalid or "
        + "issued to someone else, 410 when it expired, 409 assistant.handoff_active while a support case is "
        + "open. Retrying is safe: an active coupon keeps its code.";

    public static async Task<IResult> Chat(
        ChatRequest request,
        HttpContext http,
        UserManager<ApplicationUser> users,
        ChatService chat,
        IOptions<AssistantOptions> options,
        IOptions<JsonOptions> json,
        AppDbContext db,
        Conversations.Conversations conversations
    )
    {
        var errors = Validate(request, options.Value);
        if (errors.Count > 0)
            return ApiProblems.Validation(
                "Chat message is not valid",
                AssistantErrorCodes.ValidationFailed,
                errors
            );
        var userId = users.GetUserId(http.User)!;
        // A plain read is enough: a turn changes nothing, and a confirmation it proposes is re-checked under the lock.
        if (await SupportCases.HasOpenCaseAsync(db, userId, http.RequestAborted))
            return HandoffActive();
        // Before the model is called: a turn of an ended, idle or foreign conversation never runs.
        switch (
            await conversations.BeginTurnAsync(
                request.ConversationId!.Value,
                userId,
                http.RequestAborted
            )
        )
        {
            case Conversations.TurnAdmission.NotFound:
                return Conversations.ConversationEndpoints.NotFound();
            case Conversations.TurnAdmission.Ended:
                return Conversations.ConversationEndpoints.Ended();
        }

        var response = http.Response;
        response.ContentType = "text/event-stream; charset=utf-8";
        // no-transform: a compressing proxy (Next dev/start does it by default) would
        // otherwise gzip the stream and hold it until the end.
        response.Headers.CacheControl = "no-store, no-transform";
        response.Headers["X-Accel-Buffering"] = "no";
        response.Headers.ContentEncoding = "identity";
        var correlationId = Guid.CreateVersion7().ToString("N");
        response.Headers["X-Correlation-Id"] = correlationId;

        var aborted = http.RequestAborted;
        await using var sse = new SseWriter(response, json.Value.SerializerOptions);
        try
        {
            await response.Body.FlushAsync(aborted);
        }
        catch (Exception) when (aborted.IsCancellationRequested)
        {
            // The client left before the stream started; RunAsync records the turn as cancelled.
        }

        using var stopHeartbeat = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        var heartbeat = HeartbeatAsync(sse, options.Value.HeartbeatInterval, stopHeartbeat.Token);
        try
        {
            await chat.RunAsync(request, new ToolContext(userId), correlationId, sse, aborted);
        }
        finally
        {
            await stopHeartbeat.CancelAsync();
            await heartbeat;
        }
        return Results.Empty;
    }

    private static async Task HeartbeatAsync(
        SseWriter sse,
        TimeSpan interval,
        CancellationToken stop
    )
    {
        using var timer = new PeriodicTimer(interval);
        try
        {
            while (await timer.WaitForNextTickAsync(stop))
                await sse.HeartbeatAsync(stop);
        }
        // A heartbeat is best effort: a broken connection is reported by the turn, not here.
        catch (Exception) { }
    }

    private static List<IFieldError> Validate(ChatRequest request, AssistantOptions options)
    {
        var errors = new List<IFieldError>();
        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message))
            errors.Add(new AssistantValidationError("message", AssistantErrorCodes.RequiredField));
        else if (message.Length > options.MaxMessageLength)
            errors.Add(new AssistantValidationError("message", AssistantErrorCodes.TooLong));

        if (request.History is { } history)
        {
            if (history.Count > options.MaxHistoryItems)
                errors.Add(new AssistantValidationError("history", AssistantErrorCodes.TooLong));
            else if (
                history.Any(item =>
                    item is null
                    || item.Role is not ("user" or "assistant")
                    || string.IsNullOrWhiteSpace(item.Text)
                    || item.Text.Length > options.MaxHistoryTextLength
                )
            )
                errors.Add(
                    new AssistantValidationError("history", AssistantErrorCodes.InvalidFormat)
                );
        }
        if (request.ConversationId is null || request.ConversationId == Guid.Empty)
            errors.Add(
                new AssistantValidationError("conversationId", AssistantErrorCodes.RequiredField)
            );
        return errors;
    }

    public static async Task<Results<Ok<CouponResponse>, ProblemHttpResult>> Confirm(
        ConfirmationRequest request,
        HttpContext http,
        UserManager<ApplicationUser> users,
        ConfirmationTokens tokens,
        AppDbContext db,
        CustomerLock customerLock,
        CancellationToken cancellationToken
    )
    {
        var userId = users.GetUserId(http.User)!;
        var (check, proposal) = tokens.Read(request.Token, userId);
        if (check == TokenCheck.Invalid)
            return ApiProblems.Problem(
                StatusCodes.Status404NotFound,
                "Confirmation not found",
                AssistantErrorCodes.ConfirmationInvalid
            );
        if (check == TokenCheck.Expired)
            return ApiProblems.Problem(
                StatusCodes.Status410Gone,
                "Confirmation expired",
                AssistantErrorCodes.ConfirmationExpired
            );

        // ADR 0012: case creation takes the same lock, so the check and the activation below commit
        // either before the case exists or not at all. Runs in the idempotency transaction.
        if (!await customerLock.TryAcquireAsync(userId, cancellationToken))
            return LoyaltyProblems.CustomerBusy(http);
        if (await SupportCases.HasOpenCaseAsync(db, userId, cancellationToken))
            return HandoffActive();
        return CouponActivation.ToHttp(
            await CouponActivation.ActivateAsync(db, userId, proposal!.CouponId, cancellationToken)
        );
    }

    private static ProblemHttpResult HandoffActive() =>
        ApiProblems.Problem(
            StatusCodes.Status409Conflict,
            "The conversation is with a support agent",
            AssistantErrorCodes.HandoffActive
        );
}
