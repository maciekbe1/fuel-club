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
    public void Prune_removes_only_transcripts_and_judge_files_past_the_retention()
    {
        var folder = Path.Combine(_directory, "run-a", "transcripts");
        Directory.CreateDirectory(folder);
        var old = Path.Combine(folder, "c00000.json");
        var fresh = Path.Combine(folder, "c00001.json");
        File.WriteAllText(old, "{}");
        File.WriteAllText(fresh, "{}");
        File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-15));
        // Judge files (grades, the calibration sheet with answers) follow the same retention.
        var judge = Path.Combine(_directory, "run-a", "judge");
        Directory.CreateDirectory(judge);
        var oldSheet = Path.Combine(judge, "calibration-judge-rubric-v1-m.tsv");
        var freshGrades = Path.Combine(judge, "results-judge-rubric-v1.json");
        File.WriteAllText(oldSheet, "id");
        File.WriteAllText(freshGrades, "[]");
        File.SetLastWriteTimeUtc(oldSheet, DateTime.UtcNow.AddDays(-15));

        var removed = ChatScenarios.Prune(_directory, TimeSpan.FromDays(14), DateTimeOffset.UtcNow);

        Assert.Equal(2, removed);
        Assert.False(File.Exists(old));
        Assert.True(File.Exists(fresh));
        Assert.False(File.Exists(oldSheet));
        Assert.True(File.Exists(freshGrades));
    }

    [Fact]
    public void Scenario_weights_add_up_and_every_scenario_has_a_turn()
    {
        Assert.Equal(100, ChatScenarios.All.Sum(s => s.Weight));
        Assert.All(ChatScenarios.All, s => Assert.NotEmpty(s.Steps));
        // The known wrong questions stay as regression scenarios, apart from the reworded ones.
        Assert.Equal(
            [
                "Za co dostaję punkty?",
                "Jakie nagrody mogę wybrać?",
                "Aktywuj kupon SIM {run} rabat",
                "Jak działa zbieranie punktów?",
            ],
            ChatScenarios.All.Where(s => s.Regression).SelectMany(s => s.Messages)
        );
        Assert.Equal(
            ChatScenarios.All.Length,
            ChatScenarios.All.Select(s => s.Name).Distinct().Count()
        );
    }

    [Fact]
    public void A_rerun_keeps_the_scenario_a_conversation_was_given()
    {
        var manifest = Manifest.Create(
            ManifestPath,
            "t-1",
            "http://localhost:5301",
            "Europe/Warsaw"
        );
        var settings = new Settings("s", "c", "c", "a", "a", 1, 10, 10);
        var first = new ChatScenarios(manifest, settings, 60, regressionsEach: 3).ScenarioFor(
            0,
            "a0"
        );
        Assert.True(first.Regression);

        // Another flag value on the rerun: the stored scenario wins, the new pick does not.
        var reloaded = Manifest.Load(ManifestPath);
        var again = new ChatScenarios(reloaded, settings, 60, regressionsEach: 0).ScenarioFor(
            0,
            "a0"
        );
        Assert.Equal(first.Name, again.Name);
    }

    [Fact]
    public void Only_a_turn_that_finished_with_done_gives_a_token_to_confirm()
    {
        var cut = new TranscriptTurn { User = "Aktywuj kupon", Done = false };
        cut.Tokens.Add("cut-token");
        var transcript = new Transcript { RunId = "t-1", Scenario = "aktywacja" };
        transcript.Turns.Add(cut);

        Assert.Null(ChatScenarios.ConfirmationToken(transcript));

        var done = new TranscriptTurn { User = "Aktywuj kupon", Done = true };
        done.Tokens.Add("done-token");
        transcript.Turns.Add(done);
        Assert.Equal("done-token", ChatScenarios.ConfirmationToken(transcript));
    }

    [Fact]
    public async Task A_rerun_after_a_lost_end_keeps_the_talk_saved_before_the_end()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        var file = Path.Combine(Path.GetDirectoryName(ManifestPath)!, "c00000.json");
        var talked = new Transcript
        {
            RunId = "t-1",
            Scenario = "regulamin",
            Outcome = ChatScenarios.EndPending,
        };
        talked.Turns.Add(new TranscriptTurn { User = "Czy punkty wygasają?", Done = true });
        await ChatScenarios.SaveTranscriptAsync(file, talked, default);

        // The end went through, its answer was lost; the rerun finds the conversation ended.
        Assert.True(await ChatScenarios.CompleteEndPendingAsync(file, "ended-earlier", default));

        var saved = System
            .Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(file))
            .RootElement;
        Assert.Equal("ended-earlier", saved.GetProperty("outcome").GetString());
        Assert.Equal(1, saved.GetProperty("turns").GetArrayLength());

        // An end that was confirmed keeps its own outcome.
        talked.Outcome = "handoff";
        await ChatScenarios.SaveTranscriptAsync(file, talked, default);
        Assert.True(await ChatScenarios.CompleteEndPendingAsync(file, "ended-earlier", default));
        saved = System.Text.Json.JsonDocument.Parse(await File.ReadAllTextAsync(file)).RootElement;
        Assert.Equal("handoff", saved.GetProperty("outcome").GetString());

        // A file cut short (an interrupted write of an older version) is not trusted: rewrite it.
        await File.WriteAllTextAsync(file, "{\"runId\": \"t-1\", \"tur");
        Assert.False(await ChatScenarios.CompleteEndPendingAsync(file, "ended-earlier", default));
    }

    [Fact]
    public async Task A_token_in_the_text_is_never_written_and_fails_only_when_asked()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(ManifestPath)!);
        var file = Path.Combine(Path.GetDirectoryName(ManifestPath)!, "c00001.json");
        var turn = new TranscriptTurn
        {
            User = "Aktywuj",
            Assistant = "Token: secret-1",
            Done = true,
        };
        turn.Tokens.Add("secret-1");
        var leaked = new Transcript { RunId = "t-1", Scenario = "aktywacja" };
        leaked.Turns.Add(turn);

        // Before the end: skipped, so the end still runs; after it: the operation fails.
        Assert.False(
            await ChatScenarios.SaveTranscriptAsync(file, leaked, default, throwOnLeak: false)
        );
        Assert.False(File.Exists(file));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            ChatScenarios.SaveTranscriptAsync(file, leaked, default)
        );
        Assert.False(File.Exists(file));
    }

    [Fact]
    public void A_handoff_counts_only_from_a_turn_that_finished_with_done()
    {
        var transcript = new Transcript { RunId = "t-1", Scenario = "konsultant" };
        transcript.Turns.Add(
            new TranscriptTurn
            {
                User = "Konsultant",
                Handoff = true,
                Done = false,
            }
        );
        Assert.False(ChatScenarios.HandoffShown(transcript));

        transcript.Turns.Add(
            new TranscriptTurn
            {
                User = "Konsultant",
                Handoff = true,
                Done = true,
            }
        );
        Assert.True(ChatScenarios.HandoffShown(transcript));
    }

    [Fact]
    public void A_resumed_conversation_keeps_its_recorded_account_after_more_are_populated()
    {
        var manifest = Manifest.Create(
            ManifestPath,
            "t-1",
            "http://localhost:5301",
            "Europe/Warsaw"
        );
        SimAccount Account(int i) =>
            new()
            {
                Index = i,
                Email = $"a{i}@sim.test",
                Profile = Profile.Low,
                UserId = $"u{i}",
            };
        var two = new List<SimAccount> { Account(0), Account(1) };
        var settings = new Settings("s", "c", "c", "a", "a", 1, 10, 10);
        var chat = new ChatScenarios(manifest, settings, 60);
        // The first run started c00000..c00003 on two accounts (c00003 left pending).
        foreach (var (index, account) in chat.AssignAccounts(4, two))
            manifest.Operation($"c{index:00000}", "conversation", account!.Email);

        // A third account is populated; the resume must not move c00002 or c00003 to it.
        var three = new List<SimAccount>(two) { Account(2) };
        var resumed = chat.AssignAccounts(6, three);

        Assert.Equal(
            [
                "a0@sim.test",
                "a1@sim.test",
                "a0@sim.test",
                "a1@sim.test",
                "a1@sim.test",
                "a2@sim.test",
            ],
            resumed.Select(p => p.Account!.Email)
        );
        // A recorded account that is not usable now: the conversation fails, it is not moved.
        Assert.Null(chat.AssignAccounts(4, [Account(0)])[1].Account);
    }

    [Fact]
    public void A_pass_replays_the_base_conversation_as_a_new_one()
    {
        var manifest = Manifest.Create(
            ManifestPath,
            "t-1",
            "http://localhost:5301",
            "Europe/Warsaw"
        );
        var settings = new Settings("s", "c", "c", "a", "a", 1, 10, 10);
        SimAccount Account(int i) =>
            new()
            {
                Index = i,
                Email = $"a{i}@sim.test",
                Profile = Profile.Low,
                UserId = $"u{i}",
            };
        var baseRun = new ChatScenarios(manifest, settings, 60);
        var first = baseRun.ScenarioFor(20, "a1@sim.test");
        // A base run from before scenarios were stored: the pass reads its transcript.
        Directory.CreateDirectory(Path.Combine(_directory, "transcripts"));
        File.WriteAllText(
            Path.Combine(_directory, "transcripts", "c00021.json"),
            "{\"scenario\": \"oferty\", \"turns\": []}"
        );

        // The pass runs with other flags and more accounts: same scenario and account, new key.
        var pass = new ChatScenarios(manifest, settings, 60, regressionsEach: 0, pass: "v8");
        var (_, account) = pass.AssignAccounts(21, [Account(0), Account(1), Account(2)])[20];

        Assert.Equal(first.Name, pass.ScenarioFor(20, account!.Email).Name);
        Assert.Equal("a1@sim.test", account.Email);
        Assert.Equal("v8/c00020", pass.Key(20));
        Assert.NotEqual(
            manifest.Find("c00020")!.IdempotencyKey,
            manifest.Find("v8/c00020")!.IdempotencyKey
        );
        Assert.EndsWith(Path.Combine("passes", "v8"), pass.RunFolder);
        Assert.Equal("oferty", pass.ScenarioFor(21, "a0@sim.test").Name);
    }

    [Fact]
    public void A_comparison_checks_both_sides_with_todays_steps()
    {
        static (string, TranscriptFile) Run(
            string folder,
            string answer,
            params string[] proposals
        ) =>
            (
                $"/{folder}/transcripts/c00000.json",
                new TranscriptFile
                {
                    Scenario = "aktywacja",
                    Turns =
                    [
                        new TranscriptTurn
                        {
                            User = "Aktywuj kupon SIM t-1 rabat",
                            Assistant = answer,
                            Done = true,
                            Tools = ["list_my_coupons:ok"],
                            Confirmations = proposals.Length,
                            Proposals = [.. proposals],
                        },
                    ],
                }
            );

        var (rows, changes, _) = Comparison.Compare(
            [Run("base", "Proponuję aktywację.", "SIM t-1 hot dog")],
            [Run("pass", "Nie masz takiego kuponu do aktywacji. Który kupon aktywować?")]
        );

        Assert.Equal(new Comparison.Row("aktywacja", 1, 0, 1), Assert.Single(rows));
        Assert.True(Assert.Single(changes).PassOk);
    }

    [Fact]
    public void A_proposal_is_checked_by_its_recorded_target_and_without_one_it_fails()
    {
        var step = new Step("Aktywuj kupon SIM t-1 rabat", ProposalOnly: @"\brabat$");
        TranscriptTurn Turn(int confirmations, params string[] proposals) =>
            new()
            {
                User = step.Message,
                Assistant = "Proponuję aktywację kuponu „SIM t-1 rabat”.",
                Done = true,
                Confirmations = confirmations,
                Proposals = [.. proposals],
            };

        Assert.All(step.Check(Turn(1, "SIM t-1 rabat")), c => Assert.True(c.Ok));
        Assert.Contains(step.Check(Turn(1, "SIM t-1 hot dog")), c => !c.Ok);
        // An older transcript: a proposal with no recorded target is not taken as right.
        Assert.Contains(step.Check(Turn(1)), c => !c.Ok);
        Assert.All(step.Check(Turn(0)), c => Assert.True(c.Ok));
    }

    [Fact]
    public void A_turn_passes_only_when_every_rule_check_does()
    {
        var step = new Step(
            "Za co dostaję punkty?",
            ["search_program_rules:ok"],
            @"§\s?3\.1",
            Handoff: false
        );
        var right = new TranscriptTurn
        {
            User = step.Message,
            Assistant = "Za paliwo: Regulamin, §3.1.",
            Done = true,
            Tools = ["search_program_rules:ok"],
        };
        var wrong = new TranscriptTurn
        {
            User = step.Message,
            Assistant = "Masz 0 pkt.",
            Done = true,
            Tools = ["get_my_balance:ok"],
        };

        Assert.All(step.Check(right), c => Assert.True(c.Ok, c.Name));
        Assert.Equal(
            ["tool search_program_rules:ok", @"answer /§\s?3\.1/"],
            step.Check(wrong).Where(c => !c.Ok).Select(c => c.Name)
        );
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
