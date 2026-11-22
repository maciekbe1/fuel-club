using System.Diagnostics;
using FuelClub.Api.Modules.Assistant.Model;
using FuelClub.Api.Modules.Assistant.Tools;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Assistant.Chat;

/// <summary>
/// One chat turn: meta, then the model loop (tool calls within the limit), then exactly one of
/// done or error. Tool calls run as the session user only. Logs carry the tool name, status and
/// duration, never arguments, results or message text.
/// </summary>
public sealed class ChatService(
    IChatModel model,
    AssistantTools tools,
    IOptions<AssistantOptions> options,
    ILogger<ChatService> logger
)
{
    public async Task RunAsync(
        ChatRequest request,
        ToolContext context,
        string correlationId,
        SseWriter sse,
        CancellationToken aborted
    )
    {
        var settings = options.Value;
        using var turn = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        turn.CancelAfter(settings.TurnTimeout);

        await sse.EventAsync(
            "meta",
            new ChatMetaEvent(
                correlationId,
                model.Provider,
                model.Model,
                model.PromptVersion,
                model.Demo
            ),
            aborted
        );

        var results = new List<ToolResult>();
        var history = request.History ?? [];
        try
        {
            while (true)
            {
                // The model sees results without the confirmation token: only the client gets it.
                var seen = results.Select(r => r with { Confirmation = null }).ToList();
                var step = await model.NextAsync(
                    new ChatModelContext(request.Message!.Trim(), history, seen),
                    turn.Token
                );
                if (step is ModelStep.Reply reply)
                {
                    await sse.EventAsync("delta", new ChatDeltaEvent(reply.Text), turn.Token);
                    await sse.EventAsync("done", new ChatDoneEvent(results.Count), turn.Token);
                    return;
                }

                var call = ((ModelStep.Tool)step).Call;
                if (results.Count >= settings.MaxToolCalls)
                {
                    logger.LogWarning(
                        "Chat {CorrelationId} exceeded the tool limit ({Limit})",
                        correlationId,
                        settings.MaxToolCalls
                    );
                    await Fail(
                        sse,
                        AssistantErrorCodes.ToolLimit,
                        "Za dużo kroków w jednej odpowiedzi. Zadaj prostsze pytanie.",
                        aborted
                    );
                    return;
                }

                var result = await RunToolAsync(call, context, correlationId, turn.Token);
                results.Add(result);
                await sse.EventAsync(
                    "tool",
                    new ChatToolEvent(result.Name, result.Status),
                    turn.Token
                );
                if (result.Confirmation is { } confirmation)
                    await sse.EventAsync("confirmation", confirmation, turn.Token);
            }
        }
        catch (OperationCanceledException) when (aborted.IsCancellationRequested)
        {
            // The client left: nobody to tell.
            logger.LogInformation("Chat {CorrelationId} cancelled by the client", correlationId);
        }
        catch (OperationCanceledException)
        {
            logger.LogWarning("Chat {CorrelationId} timed out", correlationId);
            await Fail(
                sse,
                AssistantErrorCodes.Timeout,
                "Odpowiedź trwała za długo. Spróbuj ponownie.",
                aborted
            );
        }
        catch (Exception error)
        {
            logger.LogError(
                error,
                "Chat {CorrelationId} failed in the model or a tool",
                correlationId
            );
            await Fail(
                sse,
                AssistantErrorCodes.ProviderError,
                "Asystent jest chwilowo niedostępny. Spróbuj ponownie.",
                aborted
            );
        }
    }

    private async Task<ToolResult> RunToolAsync(
        ToolCall call,
        ToolContext context,
        string correlationId,
        CancellationToken turn
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(turn);
        timeout.CancelAfter(options.Value.ToolTimeout);
        var started = Stopwatch.GetTimestamp();
        var result = await tools.ExecuteAsync(call, context, timeout.Token);
        logger.LogInformation(
            "Chat {CorrelationId} tool {Tool} {Status} in {ElapsedMs} ms",
            correlationId,
            // Names outside the closed list are not logged verbatim (model output).
            AssistantTools.Names.Contains(call.Name)
                ? call.Name
                : "unknown",
            result.Status,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds
        );
        return result;
    }

    private static async Task Fail(
        SseWriter sse,
        string code,
        string message,
        CancellationToken aborted
    )
    {
        if (aborted.IsCancellationRequested)
            return;
        await sse.EventAsync("error", new ChatErrorEvent(code, message), aborted);
    }
}
