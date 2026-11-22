using System.Diagnostics;
using FuelClub.Api.Modules.Assistant.Model;
using FuelClub.Api.Modules.Assistant.Tools;
using FuelClub.Api.Modules.Assistant.Tracing;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Assistant.Chat;

/// <summary>
/// One chat turn: meta, then the model loop (tool calls within the limit), then exactly one of
/// done or error. Tool calls run as the session user only. Logs carry the tool name, status and
/// duration, never arguments, results or message text. Every turn leaves a trace (metadata only):
/// a row in assistant_turns and, when a listener is configured, an OpenTelemetry span.
/// </summary>
public sealed class ChatService(
    IChatModel model,
    AssistantTools tools,
    TracePseudonyms pseudonyms,
    IServiceScopeFactory scopes,
    TimeProvider clock,
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
        using var recorder = new TurnRecorder(
            correlationId,
            pseudonyms.For(context.UserId),
            model,
            clock.GetUtcNow()
        );
        try
        {
            await RunTurnAsync(request, context, correlationId, sse, recorder, settings, aborted);
        }
        finally
        {
            await recorder.SaveAsync(scopes, settings.TraceSaveTimeout, logger);
        }
    }

    private async Task RunTurnAsync(
        ChatRequest request,
        ToolContext context,
        string correlationId,
        SseWriter sse,
        TurnRecorder recorder,
        AssistantOptions settings,
        CancellationToken aborted
    )
    {
        using var turn = CancellationTokenSource.CreateLinkedTokenSource(aborted);
        turn.CancelAfter(settings.TurnTimeout);
        // Until a turn ends otherwise, it was cancelled (the client left, or writing failed).
        recorder.Finish(TurnOutcome.Cancelled);

        async Task Send<T>(string name, T payload, CancellationToken token)
        {
            recorder.Event();
            await sse.EventAsync(name, payload, token);
        }

        async Task Fail(string code, string message)
        {
            // The client already left: the turn is Cancelled, whatever broke after that.
            if (aborted.IsCancellationRequested)
                return;
            recorder.Finish(TurnOutcome.Error, code);
            recorder.Event();
            await sse.EventAsync("error", new ChatErrorEvent(code, message), aborted);
        }

        var results = new List<ToolResult>();
        var calls = new List<ToolCall>();
        var history = request.History ?? [];
        try
        {
            // Inside the try: a client gone before the first event ends the turn as cancelled.
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

            while (true)
            {
                // The model sees results without the confirmation token: only the client gets it.
                var seen = results.Select(r => r with { Confirmation = null }).ToList();
                var step = await model.NextAsync(
                    new ChatModelContext(request.Message!.Trim(), history, seen)
                    {
                        ToolCalls = calls,
                    },
                    turn.Token
                );
                recorder.Usage(step.Usage);
                if (step is ModelStep.Reply reply)
                {
                    await Send("delta", new ChatDeltaEvent(reply.Text), turn.Token);
                    await Send("done", new ChatDoneEvent(results.Count), turn.Token);
                    recorder.Finish(TurnOutcome.Done);
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
                        AssistantErrorCodes.ToolLimit,
                        "Za dużo kroków w jednej odpowiedzi. Zadaj prostsze pytanie."
                    );
                    return;
                }

                var result = await RunToolAsync(call, context, correlationId, recorder, turn.Token);
                results.Add(result);
                calls.Add(call);
                await Send("tool", new ChatToolEvent(result.Name, result.Status), turn.Token);
                if (result.Confirmation is { } confirmation)
                    await Send("confirmation", confirmation, turn.Token);
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
            await Fail(AssistantErrorCodes.Timeout, "Odpowiedź trwała za długo. Spróbuj ponownie.");
        }
        catch (Exception) when (aborted.IsCancellationRequested)
        {
            // A write to a closed connection (IOException and the like): the client left.
            logger.LogInformation("Chat {CorrelationId} ended: the client left", correlationId);
        }
        catch (Exception error)
        {
            logger.LogError(
                error,
                "Chat {CorrelationId} failed in the model or a tool",
                correlationId
            );
            await Fail(
                AssistantErrorCodes.ProviderError,
                "Asystent jest chwilowo niedostępny. Spróbuj ponownie."
            );
        }
    }

    private async Task<ToolResult> RunToolAsync(
        ToolCall call,
        ToolContext context,
        string correlationId,
        TurnRecorder recorder,
        CancellationToken turn
    )
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(turn);
        timeout.CancelAfter(options.Value.ToolTimeout);
        // Names outside the closed list are not logged or traced verbatim (model output).
        var name = AssistantTools.Names.Contains(call.Name) ? call.Name : "unknown";
        var started = Stopwatch.GetTimestamp();
        var result = await recorder.ToolAsync(
            name,
            () => tools.ExecuteAsync(call, context, timeout.Token)
        );
        logger.LogInformation(
            "Chat {CorrelationId} tool {Tool} {Status} in {ElapsedMs} ms",
            correlationId,
            name,
            result.Status,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds
        );
        return result;
    }
}
