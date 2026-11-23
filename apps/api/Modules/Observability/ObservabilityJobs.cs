using FuelClub.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Observability;

/// <summary>Deletes traces and security events older than their retention.</summary>
public sealed class ObservabilityRetention(AppDbContext db, IOptions<ObservabilityOptions> options)
{
    public async Task<(int Turns, int SecurityEvents, int Conversations)> PurgeAsync(
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var turnsBefore = now - options.Value.TraceRetention;
        var eventsBefore = now - options.Value.SecurityEventRetention;
        var turns = await db
            .AssistantTurns.Where(t => t.StartedAt < turnsBefore)
            .ExecuteDeleteAsync(ct);
        var events = await db
            .SecurityEvents.Where(e => e.OccurredAt < eventsBefore)
            .ExecuteDeleteAsync(ct);
        // The same retention as the turns, counted from the last activity.
        var conversations = await db
            .AssistantConversations.Where(c => c.LastActivityAt < turnsBefore)
            .ExecuteDeleteAsync(ct);
        return (turns, events, conversations);
    }
}

/// <summary>Runs retention and the anomaly rules on their intervals. A failed run is logged and retried.</summary>
public sealed class ObservabilityWorker(
    IServiceScopeFactory scopes,
    IOptions<ObservabilityOptions> options,
    AnomalyAlerts alerts,
    TimeProvider clock,
    ILogger<ObservabilityWorker> logger
) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        if (!options.Value.WorkerEnabled)
            return;
        var nextPurge = DateTimeOffset.MinValue;
        using var timer = new PeriodicTimer(options.Value.MonitorInterval, clock);
        do
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                var now = clock.GetUtcNow();
                if (now >= nextPurge)
                {
                    var (turns, events, conversations) = await scope
                        .ServiceProvider.GetRequiredService<ObservabilityRetention>()
                        .PurgeAsync(now, stop);
                    if (turns + events + conversations > 0)
                        logger.LogInformation(
                            "Retention removed {Turns} traces, {Events} security events and {Conversations} conversations",
                            turns,
                            events,
                            conversations
                        );
                    nextPurge = now + options.Value.PurgeInterval;
                }
                var rules = scope.ServiceProvider.GetRequiredService<AnomalyRules>();
                alerts.Raise(await rules.EvaluateAsync(now, stop), rules.Cooldown, now);
            }
            catch (OperationCanceledException) when (stop.IsCancellationRequested)
            {
                return;
            }
            catch (Exception error)
            {
                logger.LogWarning("Observability run failed: {Error}", error.GetType().Name);
            }
        } while (await timer.WaitForNextTickAsync(stop).ConfigureAwait(false));
    }
}
