using FuelClub.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;

namespace FuelClub.Api.Modules.Loyalty.Infrastructure;

public sealed class LoyaltyOptions
{
    public const string Section = "Loyalty";
    public static readonly TimeSpan MinLockTimeout = TimeSpan.FromMilliseconds(1);
    public static readonly TimeSpan MaxLockTimeout = TimeSpan.FromSeconds(30);

    /// <summary>How long an operation waits for another operation on the same customer.</summary>
    public TimeSpan CustomerLockTimeout { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>IANA time zone of the stations; "today" for sale dates is taken in this zone.</summary>
    public string StationTimeZone { get; set; } = "Europe/Warsaw";
}

/// <summary>Current date at the station. Uses TimeProvider so tests control the instant.</summary>
public sealed class StationClock(TimeProvider time, IOptions<LoyaltyOptions> options)
{
    private readonly TimeZoneInfo _zone = TimeZoneInfo.FindSystemTimeZoneById(
        options.Value.StationTimeZone
    );

    public DateOnly Today =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(time.GetUtcNow(), _zone).DateTime);
}

/// <summary>lock_timeout = 0 means "wait forever" in PostgreSQL, so the bounds are enforced at startup.</summary>
public sealed class LoyaltyOptionsValidator : IValidateOptions<LoyaltyOptions>
{
    public ValidateOptionsResult Validate(string? name, LoyaltyOptions options)
    {
        var failures = new List<string>();
        if (
            options.CustomerLockTimeout < LoyaltyOptions.MinLockTimeout
            || options.CustomerLockTimeout > LoyaltyOptions.MaxLockTimeout
        )
            failures.Add(
                $"{LoyaltyOptions.Section}:CustomerLockTimeout must be between {LoyaltyOptions.MinLockTimeout} and {LoyaltyOptions.MaxLockTimeout}; got {options.CustomerLockTimeout}."
            );
        if (
            string.IsNullOrWhiteSpace(options.StationTimeZone)
            || !TimeZoneInfo.TryFindSystemTimeZoneById(options.StationTimeZone, out _)
        )
            failures.Add(
                $"{LoyaltyOptions.Section}:StationTimeZone '{options.StationTimeZone}' is not a known time zone."
            );
        return failures.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(failures);
    }
}

/// <summary>
/// Serializes every operation that changes or depends on a customer's card or balance: card issue and
/// rotation now, purchase by the cashier in 3.4 (lock the card owner, then re-check that the scanned
/// card is still active). Transaction-scoped advisory lock: released on commit or rollback, never
/// earlier. Waiting is bounded by CustomerLockTimeout and honours cancellation.
/// </summary>
public sealed class CustomerLock(AppDbContext db, IOptions<LoyaltyOptions> options)
{
    /// <returns>false when the wait timed out; the transaction is then aborted and must be rolled back.</returns>
    public async Task<bool> TryAcquireAsync(string userId, CancellationToken cancellationToken)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "The customer lock is transaction-scoped; call it inside a transaction."
            );

        var timeoutMs = (long)options.Value.CustomerLockTimeout.TotalMilliseconds;
        await db.Database.ExecuteSqlRawAsync(
            $"SET LOCAL lock_timeout = {timeoutMs}",
            cancellationToken
        );
        try
        {
            await db.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT pg_advisory_xact_lock(hashtextextended({Key(userId)}, 0))",
                cancellationToken
            );
        }
        catch (PostgresException exception)
            when (exception.SqlState == PostgresErrorCodes.LockNotAvailable)
        {
            return false;
        }

        await db.Database.ExecuteSqlRawAsync(
            "SET LOCAL lock_timeout TO DEFAULT",
            cancellationToken
        );
        return true;
    }

    /// <summary>A hash collision between two users only makes them wait for each other.</summary>
    public static string Key(string userId) => "loyalty-customer:" + userId;
}
