using System.Text.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant.Confirmation;
using FuelClub.Api.Modules.Assistant.Tools;
using FuelClub.Api.Modules.Rewards.Coupons;
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
        + "Comment lines (': ping') are heartbeats. Validation errors and 429 come as ProblemDetails before "
        + "the stream starts.";

    public const string ConfirmDescription =
        "Confirms an activation proposed in the chat (the token from a confirmation event) and activates the "
        + "coupon with the same logic as /api/me/coupons/{id}/activate. 404 for a token that is invalid or "
        + "issued to someone else, 410 when it expired. Retrying is safe: an active coupon keeps its code.";

    public static async Task<IResult> Chat(
        ChatRequest request,
        HttpContext http,
        UserManager<ApplicationUser> users,
        ChatService chat,
        IOptions<AssistantOptions> options,
        IOptions<JsonOptions> json
    )
    {
        var errors = Validate(request, options.Value);
        if (errors.Count > 0)
            return ApiProblems.Validation(
                "Chat message is not valid",
                AssistantErrorCodes.ValidationFailed,
                errors
            );

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
        await response.Body.FlushAsync(aborted);

        using var stopHeartbeat = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        var heartbeat = HeartbeatAsync(sse, options.Value.HeartbeatInterval, stopHeartbeat.Token);
        try
        {
            await chat.RunAsync(
                request,
                new ToolContext(users.GetUserId(http.User)!),
                correlationId,
                sse,
                aborted
            );
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
        return errors;
    }

    public static async Task<Results<Ok<CouponResponse>, ProblemHttpResult>> Confirm(
        ConfirmationRequest request,
        HttpContext http,
        UserManager<ApplicationUser> users,
        ConfirmationTokens tokens,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var userId = users.GetUserId(http.User)!;
        var (check, proposal) = tokens.Read(request.Token, userId);
        return check switch
        {
            TokenCheck.Invalid => ApiProblems.Problem(
                StatusCodes.Status404NotFound,
                "Confirmation not found",
                AssistantErrorCodes.ConfirmationInvalid
            ),
            TokenCheck.Expired => ApiProblems.Problem(
                StatusCodes.Status410Gone,
                "Confirmation expired",
                AssistantErrorCodes.ConfirmationExpired
            ),
            _ => CouponActivation.ToHttp(
                await CouponActivation.ActivateAsync(
                    db,
                    userId,
                    proposal!.CouponId,
                    cancellationToken
                )
            ),
        };
    }
}
