using FuelClub.Simulator;

namespace FuelClub.Api.Tests;

/// <summary>The simulator's own guarantees (ADR 0017); its API traffic is exercised by a run.</summary>
public sealed class SimulatorTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"sim-{Guid.NewGuid():N}"
    );
    private string ManifestPath => Path.Combine(_directory, "manifest.json");

    [Fact]
    public void An_operation_keeps_its_idempotency_key_across_runs()
    {
        var first = Manifest.Create(ManifestPath, "t-1", "http://localhost:5301", "Europe/Warsaw");
        var operation = first.Operation("a0000:purchase:0", "purchase", "a0000");
        operation.Status = OperationStatus.Pending;
        first.Save();

        var again = Manifest.Load(ManifestPath).Operation("a0000:purchase:0", "purchase", "a0000");

        Assert.Equal(operation.IdempotencyKey, again.IdempotencyKey);
        Assert.Equal(OperationStatus.Pending, again.Status);
    }

    [Fact]
    public void A_manifest_is_never_created_over_an_existing_one()
    {
        Manifest.Create(ManifestPath, "t-1", "http://localhost:5301", "Europe/Warsaw");

        Assert.Throws<InvalidOperationException>(() =>
            Manifest.Create(ManifestPath, "t-1", "http://localhost:5301", "Europe/Warsaw")
        );
    }

    [Fact]
    public async Task Cleanup_refuses_any_database_but_the_demo_one()
    {
        var manifest = Manifest.Create(
            ManifestPath,
            "t-1",
            "http://localhost:5301",
            "Europe/Warsaw"
        );
        manifest.Accounts.Add(
            new SimAccount
            {
                Index = 0,
                Email = $"sim-t-1-0000@{Population.EmailDomain}",
                Profile = Profile.Zero,
                UserId = Guid.NewGuid().ToString(),
            }
        );

        var result = await Cleanup.RunAsync(
            manifest,
            Environment.GetEnvironmentVariable("FUELCLUB_TEST_DATABASE")!,
            CancellationToken.None
        );

        Assert.Equal(2, result);
        Assert.Null(Manifest.Load(ManifestPath).CleanedAt);
    }

    [Fact]
    public void Prune_removes_only_transcripts_past_the_retention()
    {
        var folder = Path.Combine(_directory, "run-a", "transcripts");
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "c00000.json");
        var fresh = Path.Combine(folder, "c00001.json");
        File.WriteAllText(old, "{}");
        File.WriteAllText(fresh, "{}");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-15));

        var removed = ChatScenarios.Prune(_directory, TimeSpan.FromDays(14), DateTimeOffset.UtcNow);

        Assert.Equal(1, removed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
    }

    [Fact]
    public void Scenario_weights_add_up_and_every_scenario_has_a_turn()
    {
        Assert.Equal(100, ChatScenarios.All.Sum(s => s.Weight));
        Assert.All(ChatScenarios.All, s => Assert.NotEmpty(s.Messages));
        Assert.Equal(
            ChatScenarios.All.Length,
            ChatScenarios.All.Select(s => s.Name).Distinct().Count()
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
