using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace FuelClub.Api.Tests;

/// <summary>System time unless a test sets an instant. Use instants after the real "now", so data protection keys and cookies stay valid.</summary>
public sealed class MutableTimeProvider : TimeProvider
{
    private DateTimeOffset? _now;

    public void Set(DateTimeOffset now) => _now = now;

    public void Reset() => _now = null;

    public override DateTimeOffset GetUtcNow() => _now ?? System.GetUtcNow();
}

/// <summary>
/// Test-only EF interceptor: an armed gate pauses the first matching non-query command right after it
/// ran, until the test releases it. Lets a test force an interleaving (e.g. "A holds the customer
/// lock while B starts") without relying on PostgreSQL queue order.
/// </summary>
public sealed class CustomerLockGate : DbCommandInterceptor
{
    private readonly object _sync = new();
    private readonly List<(Func<DbCommand, bool> Matches, Gate Gate)> _armed = [];

    /// <summary>Pauses the command that has just acquired the customer lock with this key.</summary>
    public Gate Arm(string lockKey) =>
        ArmWhen(command => command.CommandText.Contains("pg_advisory_xact_lock", StringComparison.Ordinal)
            && command.Parameters.Cast<DbParameter>().Any(parameter => parameter.Value as string == lockKey));

    public Gate ArmWhen(Func<DbCommand, bool> matches)
    {
        Gate? gate = null;
        gate = new Gate(() => { lock (_sync) _armed.RemoveAll(entry => entry.Gate == gate); });
        lock (_sync)
            _armed.Add((matches, gate));
        return gate;
    }

    public override async ValueTask<int> NonQueryExecutedAsync(
        DbCommand command, CommandExecutedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        Gate? hit = null;
        lock (_sync)
        {
            var index = _armed.FindIndex(entry => entry.Matches(command));
            if (index >= 0)
            {
                hit = _armed[index].Gate;
                _armed.RemoveAt(index);
            }
        }
        if (hit is not null)
            await hit.HoldAsync();
        return result;
    }

    public sealed class Gate(Action disarm) : IDisposable
    {
        private readonly TaskCompletionSource _acquired = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _released = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task Acquired => _acquired.Task;

        public void Release() => _released.TrySetResult();

        internal Task HoldAsync()
        {
            _acquired.TrySetResult();
            return _released.Task;
        }

        public void Dispose()
        {
            disarm();
            Release();
        }
    }
}
