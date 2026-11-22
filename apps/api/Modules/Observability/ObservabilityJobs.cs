using FuelClub.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Observability;

/// <summary>Deletes traces and security events older than their retention.</summary>
public sealed class ObservabilityRetention(AppDbContext db, IOptions<ObservabilityOptions> options)
{
    public async Task<(int Turns, int SecurityEvents)> PurgeAsync(
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
        return (turns, events);
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
                    var (turns, events) = await scope
                        .ServiceProvider.GetRequiredService<ObservabilityRetention>()
                        .PurgeAsync(now, stop);
                    if (turns + events > 0)
                        logger.LogInformation(
                            "Retention removed {Turns} traces and {Events} security events",
                            turns,
                            events
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
