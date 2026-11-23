using System.Diagnostics;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Assistant.Model;
using FuelClub.Api.Modules.Assistant.Tools;
using A = FuelClub.Api.Modules.Assistant.Tracing.AssistantTelemetry.Attributes;

namespace FuelClub.Api.Modules.Assistant.Tracing;

/// <summary>
/// Collects one turn's metadata while it runs, then writes the trace row and ends the span. Writing
/// happens after the last event was sent; a failure is logged and never reaches the user.
/// </summary>
public sealed class TurnRecorder : IDisposable
{
    private readonly AssistantTurn _turn;
    private readonly long _started = Stopwatch.GetTimestamp();
    private readonly Activity? _activity;
    private readonly Activity? _previous;

    public TurnRecorder(
        string correlationId,
        string userPseudonym,
        IChatModel model,
        DateTimeOffset now,
        Guid? conversationId = null,
        string? runId = null,
        bool synthetic = false
    )
    {
        _turn = new AssistantTurn
        {
            Id = Guid.CreateVersion7(),
            CorrelationId = correlationId,
            UserPseudonym = userPseudonym,
            Provider = model.Provider,
            Model = model.Model,
            PromptVersion = model.PromptVersion,
            Demo = model.Demo,
            StartedAt = now,
            ConversationId = conversationId,
            Synthetic = synthetic,
            RunId = synthetic ? runId : null,
        };
        // A root span: the HTTP request activity is not exported and is not sampled, and a
        // parent-based sampler would drop the turn with it.
        // It links to the request instead, so the two traces can be matched.
        _previous = Activity.Current;
        Activity.Current = null;
        _activity = AssistantTelemetry.Source.StartActivity(
            AssistantTelemetry.TurnSpan,
            ActivityKind.Internal,
            parentContext: default,
            links: _previous is null ? null : [new ActivityLink(_previous.Context)]
        );
        if (_activity is null)
            Activity.Current = _previous;
        _activity?.SetTag(A.CorrelationId, correlationId);
        _activity?.SetTag(A.UserPseudonym, userPseudonym);
        _activity?.SetTag(A.Provider, model.Provider);
        _activity?.SetTag(A.Model, model.Model);
        _activity?.SetTag(A.PromptVersion, model.PromptVersion);
        _activity?.SetTag(A.Demo, model.Demo);
    }

    public AssistantTurn Turn => _turn;

    /// <summary>The handoff event reached the client in this turn.</summary>
    public void HandoffProposed() => _turn.HandoffProposed = true;

    private int Elapsed() => (int)Stopwatch.GetElapsedTime(_started).TotalMilliseconds;

    /// <summary>Any event after meta: the first one sets the time to first event.</summary>
    public void Event() => _turn.FirstEventMs ??= Elapsed();

    private bool _usageUnknown;

    /// <summary>
    /// Sums what the provider reports. One model call without usage makes the turn's total unknown
    /// (null): a partial sum would look like the full cost.
    /// </summary>
    public void Usage(TokenUsage? usage)
    {
        if (_usageUnknown)
            return;
        if (usage is null)
        {
            _usageUnknown = true;
            _turn.InputTokens = null;
            _turn.OutputTokens = null;
            return;
        }
        _turn.InputTokens = (_turn.InputTokens ?? 0) + usage.InputTokens;
        _turn.OutputTokens = (_turn.OutputTokens ?? 0) + usage.OutputTokens;
    }

    /// <summary>Runs a tool inside its own span; the name is already one from the closed list.</summary>
    public async Task<ToolResult> ToolAsync(string name, Func<Task<ToolResult>> run)
    {
        using var span = AssistantTelemetry.Source.StartActivity(AssistantTelemetry.ToolSpan);
        span?.SetTag(A.ToolName, name);
        var started = Stopwatch.GetTimestamp();
        void Record(string status)
        {
            span?.SetTag(A.ToolStatus, status);
            _turn.Tools.Add(
                new TurnToolCall
                {
                    Name = name,
                    Status = status,
                    DurationMs = (int)Stopwatch.GetElapsedTime(started).TotalMilliseconds,
                }
            );
            _turn.ToolCalls = _turn.Tools.Count;
        }
        try
        {
            var result = await run();
            Record(result.Status);
            return result;
        }
        catch (Exception error)
        {
            // The tool that hung or failed stays in the trace; the turn decides what to tell the user.
            Record(error is OperationCanceledException ? "timeout" : ToolStatus.Error);
            span?.SetStatus(ActivityStatusCode.Error);
            throw;
        }
    }

    public void Finish(TurnOutcome outcome, string? errorCode = null)
    {
        _turn.Outcome = outcome;
        _turn.ErrorCode = errorCode;
    }

    /// <summary>
    /// Writes the row in its own scope (nothing a tool left in the request's context is committed)
    /// and within a short timeout: the stream ends only after this, so a slow database must not
    /// hold it open.
    /// </summary>
    public async Task SaveAsync(IServiceScopeFactory scopes, TimeSpan timeout, ILogger logger)
    {
        _turn.TotalMs = Elapsed();
        _activity?.SetTag(A.Outcome, _turn.Outcome.ToString().ToLowerInvariant());
        _activity?.SetTag(A.ToolCalls, _turn.ToolCalls);
        if (_turn.FirstEventMs is { } first)
            _activity?.SetTag(A.FirstEventMs, first);
        if (_turn.ErrorCode is { } code)
        {
            _activity?.SetTag(A.ErrorType, code);
            _activity?.SetStatus(ActivityStatusCode.Error);
        }
        if (_turn.InputTokens is { } input)
            _activity?.SetTag(A.InputTokens, input);
        if (_turn.OutputTokens is { } output)
            _activity?.SetTag(A.OutputTokens, output);
        try
        {
            // Not the request's token: the client may be gone, the trace is still written.
            using var limit = new CancellationTokenSource(timeout);
            await using var scope = scopes.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.AssistantTurns.Add(_turn);
            await db.SaveChangesAsync(limit.Token);
        }
        catch (Exception error)
        {
            logger.LogWarning(
                "Chat {CorrelationId} trace not saved: {Error}",
                _turn.CorrelationId,
                error.GetType().Name
            );
        }
    }

    public void Dispose()
    {
        if (_activity is null)
            return;
        _activity.Dispose();
        Activity.Current = _previous;
    }
}
