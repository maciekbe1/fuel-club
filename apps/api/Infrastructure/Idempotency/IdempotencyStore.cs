using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FuelClub.Api.Infrastructure.Idempotency;

public sealed class IdempotencyOptions
{
    public const string Section = "Idempotency";

    public static readonly TimeSpan MinLockTimeout = TimeSpan.FromMilliseconds(1);
    public static readonly TimeSpan MaxLockTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long a request waits for a concurrent request holding the same key.</summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromSeconds(5);
}

/// <summary>
/// PostgreSQL treats lock_timeout = 0 as "wait forever", and sub-millisecond values round down to 0,
/// so the value must be at least 1 ms. The upper bound keeps a waiting request from holding a
/// database connection for long.
/// </summary>
public sealed class IdempotencyOptionsValidator : IValidateOptions<IdempotencyOptions>
{
    public ValidateOptionsResult Validate(string? name, IdempotencyOptions options) =>
        options.LockTimeout >= IdempotencyOptions.MinLockTimeout
        && options.LockTimeout <= IdempotencyOptions.MaxLockTimeout
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(
                $"{IdempotencyOptions.Section}:LockTimeout must be between {IdempotencyOptions.MinLockTimeout} and {IdempotencyOptions.MaxLockTimeout}; got {options.LockTimeout}."
            );
}

public sealed record StoredResponse(
    int StatusCode,
    string? ContentType,
    string? Location,
    byte[] Body
);

public enum ReservationOutcome
{
    Reserved,
    Replay,
    KeyReused,
    InProgress,
}

public sealed record Reservation(ReservationOutcome Outcome, StoredResponse? Response = null);

/// <summary>
/// Owns the transaction of an idempotent operation (one per request scope).
/// Reserve: begin transaction, insert the key row (waits on a concurrent holder up to LockTimeout),
/// create a savepoint. The operation then runs inside the same transaction through the scoped
/// <see cref="AppDbContext"/>; handlers must not begin their own transaction.
/// Complete: success keeps the operation's changes; a stored failure rolls back to the savepoint
/// (the key row stays reserved) and clears EF tracking. Either way the response is written to the
/// key row and the transaction commits. Abandon: rolls everything back, nothing is stored.
/// </summary>
public sealed class IdempotencyStore(AppDbContext db, IOptions<IdempotencyOptions> options)
    : IAsyncDisposable
{
    private const string Savepoint = "idempotent_operation";

    private IDbContextTransaction? _transaction;
    private string? _userId;
    private Guid _key;

    public async Task<Reservation> ReserveAsync(
        string userId,
        Guid key,
        byte[] fingerprint,
        CancellationToken cancellationToken
    )
    {
        if (_transaction is not null)
            throw new InvalidOperationException(
                "An idempotent operation is already reserved in this scope."
            );

        var transaction = await db.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        int inserted;
        try
        {
            var timeoutMs = (long)options.Value.LockTimeout.TotalMilliseconds;
            _ = await db.Database.ExecuteSqlRawAsync(
                $"SET LOCAL lock_timeout = {timeoutMs}",
                cancellationToken
            );
            inserted = await db.Database.ExecuteSqlInterpolatedAsync(
                $"""
                INSERT INTO "IdempotencyRecords" ("UserId", "IdempotencyKey", "Fingerprint")
                VALUES ({userId}, {key}, {fingerprint})
                ON CONFLICT ("UserId", "IdempotencyKey") DO NOTHING
                """,
                cancellationToken
            );
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            await transaction.DisposeAsync();
            return new Reservation(ReservationOutcome.InProgress);
        }
        catch
        {
            await transaction.DisposeAsync();
            throw;
        }

        if (inserted == 0)
        {
            // The key was committed by an earlier request; read its outcome outside this transaction.
            await transaction.DisposeAsync();
            var existing = await db.Set<IdempotencyRecord>()
                .AsNoTracking()
                .SingleAsync(
                    record => record.UserId == userId && record.IdempotencyKey == key,
                    cancellationToken
                );

            if (!existing.Fingerprint.AsSpan().SequenceEqual(fingerprint))
                return new Reservation(ReservationOutcome.KeyReused);

            if (existing.StatusCode is not { } status)
                throw new InvalidOperationException(
                    "Committed idempotency record has no stored response."
                );

            return new Reservation(
                ReservationOutcome.Replay,
                new StoredResponse(
                    status,
                    existing.ContentType,
                    existing.Location,
                    existing.Body ?? []
                )
            );
        }

        _ = await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL lock_timeout TO DEFAULT",
            cancellationToken
        );
        await transaction.CreateSavepointAsync(Savepoint, cancellationToken);
        _transaction = transaction;
        _userId = userId;
        _key = key;
        return new Reservation(ReservationOutcome.Reserved);
    }

    /// <summary>
    /// Stores the response and commits. Not cancellable on purpose: once the operation ran, its
    /// outcome must be committed even if the client has gone, so a retry replays it.
    /// </summary>
    public async Task CompleteAsync(StoredResponse response, bool keepOperationChanges)
    {
        var transaction =
            _transaction ?? throw new InvalidOperationException("Nothing is reserved.");

        if (!keepOperationChanges)
        {
            await transaction.RollbackToSavepointAsync(Savepoint);
            db.ChangeTracker.Clear();
        }

        var updated = await db.Set<IdempotencyRecord>()
            .Where(record => record.UserId == _userId && record.IdempotencyKey == _key)
            .ExecuteUpdateAsync(setters =>
                setters
                    .SetProperty(record => record.StatusCode, response.StatusCode)
                    .SetProperty(record => record.ContentType, response.ContentType)
                    .SetProperty(record => record.Location, response.Location)
                    .SetProperty(record => record.Body, response.Body)
            );
        if (updated != 1)
            throw new InvalidOperationException("Reserved idempotency record was not found.");

        await transaction.CommitAsync();
        await ReleaseAsync();
    }

    public async Task AbandonAsync()
    {
        if (_transaction is null)
            return;
        await _transaction.RollbackAsync();
        db.ChangeTracker.Clear();
        await ReleaseAsync();
    }

    private async Task ReleaseAsync()
    {
        if (_transaction is not null)
            await _transaction.DisposeAsync();
        _transaction = null;
    }

    public async ValueTask DisposeAsync() => await ReleaseAsync();
}
