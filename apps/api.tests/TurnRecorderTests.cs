using System.Diagnostics;
using FuelClub.Api.Modules.Assistant;
using FuelClub.Api.Modules.Assistant.Model;
using FuelClub.Api.Modules.Assistant.Tools;
using FuelClub.Api.Modules.Assistant.Tracing;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;

namespace FuelClub.Api.Tests;

/// <summary>The recorder and the options rules without a database or a host.</summary>
public sealed class TurnRecorderTests : IDisposable
{
    private readonly List<Activity> _spans = [];
    private readonly ActivityListener _listener;

    public TurnRecorderTests()
    {
        _listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == AssistantTelemetry.SourceName,
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = span =>
            {
                lock (_spans)
                    _spans.Add(span);
            },
        };
        ActivitySource.AddActivityListener(_listener);
    }

    public void Dispose() => _listener.Dispose();

    private sealed class UnitModel : IChatModel
    {
        public string Provider => "test";
        public string Model => "unit";
        public string PromptVersion => "unit-v1";
        public bool Demo => false;

        public Task<ModelStep> NextAsync(ChatModelContext context, CancellationToken ct) =>
            throw new NotSupportedException();
    }

    private static TurnRecorder Recorder(string id) =>
        new(id, "pseudonym", new UnitModel(), DateTimeOffset.UtcNow);

    [Fact]
    public async Task A_tool_that_times_out_or_fails_stays_in_the_trace_with_an_error_span()
    {
        var id = Guid.NewGuid().ToString("N");
        using var recorder = Recorder(id);

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            recorder.ToolAsync(
                AssistantTools.GetMyBalance,
                () => throw new OperationCanceledException()
            )
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recorder.ToolAsync(
                AssistantTools.ListMyCoupons,
                () => throw new InvalidOperationException("db down")
            )
        );

        Assert.Equal(
            ["get_my_balance:timeout", "list_my_coupons:error"],
            recorder.Turn.Tools.Select(t => $"{t.Name}:{t.Status}")
        );
        Assert.Equal(2, recorder.Turn.ToolCalls);
        List<Activity> tools;
        lock (_spans)
            tools = _spans.Where(s => s.OperationName == AssistantTelemetry.ToolSpan).ToList();
        Assert.Contains(
            tools,
            s =>
                (string?)s.GetTagItem(AssistantTelemetry.Attributes.ToolStatus) == "timeout"
                && s.Status == ActivityStatusCode.Error
        );
    }

    [Fact]
    public void One_model_call_without_usage_makes_the_total_unknown()
    {
        using var known = Recorder(Guid.NewGuid().ToString("N"));
        known.Usage(new TokenUsage(10, 1));
        known.Usage(new TokenUsage(5, 2));
        Assert.Equal((15, 3), (known.Turn.InputTokens, known.Turn.OutputTokens));

        using var partial = Recorder(Guid.NewGuid().ToString("N"));
        partial.Usage(new TokenUsage(10, 1));
        partial.Usage(null);
        partial.Usage(new TokenUsage(5, 2));
        Assert.Null(partial.Turn.InputTokens);
        Assert.Null(partial.Turn.OutputTokens);
    }

    [Fact]
    public void The_turn_span_is_a_root_linked_to_the_request_and_restores_it()
    {
        using var request = new Activity("request").Start();
        var id = Guid.NewGuid().ToString("N");

        using (var recorder = Recorder(id))
        {
            var turn = Activity.Current!;
            Assert.Equal(AssistantTelemetry.TurnSpan, turn.OperationName);
            Assert.Null(turn.Parent);
            Assert.NotEqual(request.TraceId, turn.TraceId);
            Assert.Equal(request.TraceId, Assert.Single(turn.Links).Context.TraceId);
        }

        Assert.Same(request, Activity.Current);
    }

    [Theory]
    [InlineData("Development", null, true)]
    [InlineData("Production", null, false)]
    [InlineData("Production", "c2hvcnQ=", false)]
    [InlineData("Production", "MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTIzNDU2Nzg5MDE=", true)]
    public void Pseudonym_key_is_required_outside_development_and_must_be_32_bytes(
        string environment,
        string? key,
        bool valid
    )
    {
        var validator = new AssistantOptionsValidator(
            new HostingEnvironment { EnvironmentName = environment }
        );

        var result = validator.Validate(null, new AssistantOptions { PseudonymKey = key });

        Assert.Equal(valid, result.Succeeded);
    }
}
