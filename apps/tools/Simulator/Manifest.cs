using System.Text.Json;
using System.Text.Json.Serialization;

namespace FuelClub.Simulator;

/// <summary>
/// One run (ADR 0017): accounts and every operation with its idempotency key and outcome. A rerun
/// with the same manifest repeats pending operations with the same keys and skips done ones, so
/// nothing is doubled. No passwords, cookies, card or coupon codes are stored here.
/// </summary>
public sealed class Manifest
{
    public required string RunId { get; init; }
    public required string ApiUrl { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    /// <summary>
    /// The station's local date when the run started (Loyalty:StationTimeZone): every purchase of
    /// the run, rerun included, sends this one, so a replay has the same body.
    /// </summary>
    public required DateOnly SaleDate { get; init; }
    public DateTimeOffset? CleanedAt { get; set; }
    public List<SimAccount> Accounts { get; init; } = [];
    public List<Operation> Operations { get; init; } = [];

    /// <summary>Offers this run created (removed with the run when no one else uses them).</summary>
    public List<Guid> OfferIds { get; init; } = [];

    [JsonIgnore]
    public string Path { get; private set; } = "";

    public static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() },
    };

    private readonly Lock _lock = new();

    public static Manifest Load(string path)
    {
        var manifest =
            JsonSerializer.Deserialize<Manifest>(File.ReadAllText(path), Json)
            ?? throw new InvalidDataException($"Empty manifest: {path}");
        manifest.Path = path;
        return manifest;
    }

    public static Manifest Create(string path, string runId, string apiUrl, string stationTimeZone)
    {
        if (File.Exists(path))
            throw new InvalidOperationException($"Manifest already exists: {path}");
        Directory.CreateDirectory(
            System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path))!
        );
        var manifest = new Manifest
        {
            RunId = runId,
            ApiUrl = apiUrl,
            CreatedAt = DateTimeOffset.UtcNow,
            SaleDate = DateOnly.FromDateTime(
                TimeZoneInfo
                    .ConvertTime(
                        DateTimeOffset.UtcNow,
                        TimeZoneInfo.FindSystemTimeZoneById(stationTimeZone)
                    )
                    .DateTime
            ),
        };
        manifest.Path = path;
        manifest.Save();
        return manifest;
    }

    /// <summary>The operation for this key, created (with a fresh idempotency key) on first use.</summary>
    public Operation Operation(string key, string kind, string account)
    {
        lock (_lock)
        {
            var existing = Operations.FirstOrDefault(o => o.Key == key);
            if (existing is not null)
                return existing;
            var operation = new Operation
            {
                Key = key,
                Kind = kind,
                Account = account,
                IdempotencyKey = Guid.NewGuid().ToString(),
            };
            Operations.Add(operation);
            SaveLocked();
            return operation;
        }
    }

    public void Save()
    {
        lock (_lock)
            SaveLocked();
    }

    /// <summary>Atomic: a crash never leaves half a manifest.</summary>
    private void SaveLocked()
    {
        var temp = Path + ".tmp";
        File.WriteAllText(temp, JsonSerializer.Serialize(this, Json));
        File.Move(temp, Path, overwrite: true);
    }
}

public sealed class SimAccount
{
    public required int Index { get; init; }
    public required string Email { get; init; }
    public required Profile Profile { get; init; }
    public string? UserId { get; set; }
}

/// <summary>Points and coupons the account ends with (the population's distribution).</summary>
public enum Profile
{
    Zero,
    Low,
    Medium,
    High,
}

public enum OperationStatus
{
    /// <summary>Sent or about to be sent, outcome unknown: a rerun repeats it with the same key.</summary>
    Pending,
    Done,
    Failed,
}

public sealed class Operation
{
    public required string Key { get; init; }
    public required string Kind { get; init; }
    public required string Account { get; init; }
    public required string IdempotencyKey { get; init; }
    public OperationStatus Status { get; set; }

    /// <summary>For a conversation: the scenario it was given the first time (reruns keep it).</summary>
    public string? Scenario { get; set; }

    /// <summary>A non-secret result, e.g. the coupon or purchase id.</summary>
    public string? Result { get; set; }
    public string? Error { get; set; }
}
