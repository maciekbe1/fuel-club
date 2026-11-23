using System.Net;
using System.Text.Json;
using FuelClub.Simulator;

namespace FuelClub.Api.Tests;

/// <summary>The offline judge (P16d): samples, grade parsing, judge failures apart, cost limit, calibration.</summary>
public sealed class JudgeTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        $"judge-{Guid.NewGuid():N}"
    );
    private static readonly JudgePrice Price = new("p-test", 0.30m, 2.50m);

    private static TranscriptTurn Turn(string user, bool ok, bool done = true) =>
        new()
        {
            User = user,
            Assistant = $"odpowiedź na: {user}",
            Done = done,
            Checks = [new TurnCheck("done", done), new TurnCheck("answer", ok)],
        };

    private static (string, TranscriptFile) Transcript(
        string name,
        bool regression,
        string? outcome,
        params TranscriptTurn[] turns
    ) =>
        (
            $"/x/transcripts/{name}.json",
            new TranscriptFile
            {
                RunId = "r",
                Scenario = regression ? "regresja" : "regulamin",
                Regression = regression,
                Outcome = outcome,
                Turns = [.. turns],
            }
        );

    private static List<JudgeItem> Items() =>
        JudgeSampling.Select(
            "r",
            [
                Transcript("c00000", true, "ended", Turn("Za co punkty?", ok: false)),
                Transcript("c00001", false, "ended", Turn("a", true), Turn("b", false)),
                Transcript("c00002", false, "ended", Turn("c", true), Turn("d", true, done: false)),
                Transcript("c00003", false, ChatScenarios.EndPending, Turn("e", false)),
            ],
            random: 1,
            errors: 5,
            regressions: 5
        );

    [Fact]
    public void Samples_are_apart_deterministic_and_skip_unfinished_talk()
    {
        var items = Items();

        Assert.Equal(items.Select(i => i.Id), Items().Select(i => i.Id));
        Assert.Equal(items.Count, items.Select(i => i.Id).Distinct().Count());
        Assert.Equal(["c00000#0"], items.Where(i => i.Sample == "regression").Select(i => i.Id));
        Assert.Single(items, i => i.Sample == "random");
        // Errors: failing turns not drawn at random; never the cut-off turn or end-pending talk.
        Assert.All(items.Where(i => i.Sample == "errors"), i => Assert.False(i.RuleChecksOk));
        Assert.DoesNotContain(items, i => i.Id is "c00002#1" or "c00003#0");
        // The turn before is context for the next one.
        Assert.Equal("a", Assert.Single(items.Single(i => i.Id == "c00001#1").Before).User);
    }

    private static string Answer(object grade, bool usage = true) =>
        JsonSerializer.Serialize(
            new
            {
                candidates = new[]
                {
                    new
                    {
                        content = new
                        {
                            parts = new object[]
                            {
                                new { text = "rozważam...", thought = true },
                                new { text = JsonSerializer.Serialize(grade) },
                            },
                        },
                    },
                },
                usageMetadata = usage
                    ? new
                    {
                        promptTokenCount = 1000,
                        candidatesTokenCount = 50,
                        thoughtsTokenCount = 150,
                    }
                    : null,
            }
        );

    private sealed class FakeGemini(params (HttpStatusCode Status, string Body)[] answers)
        : HttpMessageHandler
    {
        private readonly Queue<(HttpStatusCode, string)> _answers = new(answers);
        public List<HttpRequestMessage> Sent { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken
        )
        {
            Sent.Add(request);
            var (status, body) = _answers.Dequeue();
            return Task.FromResult(
                new HttpResponseMessage(status) { Content = new StringContent(body) }
            );
        }
    }

    private static GeminiJudge Judge(FakeGemini fake) =>
        new(
            new HttpClient(fake) { BaseAddress = new Uri("https://gemini.test/v1beta/") },
            "test-key",
            "judge-model",
            "§3.1 Punkty za paliwo."
        );

    [Fact]
    public async Task A_grade_gives_a_rule_verdict_and_the_cost_of_its_reported_usage()
    {
        var fake = new FakeGemini(
            (
                HttpStatusCode.OK,
                Answer(
                    new
                    {
                        answers_question = "no",
                        grounded = "not_applicable",
                        action = "inappropriate",
                        target = "not_applicable",
                        reason = "Podaje saldo zamiast zasad.",
                    }
                )
            )
        );
        var budget = new JudgeBudget(1m, Price, GeminiJudge.MaxOutputTokens);

        var result = await Judge(fake).GradeAsync(Items()[0], budget, default);

        Assert.Equal(("judged", "fail"), (result.Status, result.Verdict));
        // Thinking tokens are output: 50 + 150.
        Assert.Equal((1000, 200), (result.InputTokens, result.OutputTokens));
        Assert.Equal(Price.Cost(1000, 200), result.CostUsd);
        Assert.Equal(budget.SpentUsd, result.CostUsd);
        // The key goes in a header, never in the URL.
        var sent = Assert.Single(fake.Sent);
        Assert.DoesNotContain("test-key", sent.RequestUri!.ToString());
        Assert.Equal("test-key", sent.Headers.GetValues("x-goog-api-key").Single());
    }

    [Fact]
    public async Task A_judge_failure_is_apart_from_a_negative_grade_and_counts_at_worst_case()
    {
        var fake = new FakeGemini(
            (HttpStatusCode.InternalServerError, "{}"),
            (
                HttpStatusCode.OK,
                Answer(
                    new
                    {
                        answers_question = "maybe",
                        grounded = "yes",
                        action = "appropriate",
                        target = "not_applicable",
                        reason = "x",
                    }
                )
            ),
            (HttpStatusCode.OK, "{\"candidates\": []}")
        );
        var judge = Judge(fake);
        var budget = new JudgeBudget(1m, Price, GeminiJudge.MaxOutputTokens);
        var item = Items()[0];

        var failed = await judge.GradeAsync(item, budget, default);
        var outside = await judge.GradeAsync(item, budget, default);
        var empty = await judge.GradeAsync(item, budget, default);

        Assert.Equal(("judge_error", "http_500"), (failed.Status, failed.ErrorKind));
        Assert.Null(failed.Verdict);
        Assert.Equal(budget.WorstCase(judge.Characters(item)), failed.CostUsd);
        Assert.Equal(("judge_error", "invalid_format"), (outside.Status, outside.ErrorKind));
        Assert.Equal(("judge_error", "invalid_format"), (empty.Status, empty.ErrorKind));
    }

    private static JudgeItem Activation(TranscriptTurn turn) =>
        JudgeSampling.Select(
            "r",
            [
                (
                    "/x/transcripts/c00009.json",
                    new TranscriptFile
                    {
                        RunId = "r",
                        Scenario = "aktywacja",
                        Outcome = "ended",
                        Turns = [turn],
                    }
                ),
            ],
            random: 1,
            errors: 1,
            regressions: 0
        )[0];

    private static TranscriptTurn Proposed(int confirmations, params string[] proposals) =>
        new()
        {
            User = "Aktywuj kupon SIM r rabat",
            Assistant = "Proponuję aktywację kuponu.",
            Done = true,
            Tools = ["list_my_coupons:ok", "propose_coupon_activation:ok"],
            Confirmations = confirmations,
            Proposals = [.. proposals],
            Checks = [new TurnCheck("done", true)],
        };

    private static string Grade(string target) =>
        Answer(
            new
            {
                answers_question = "yes",
                grounded = "not_applicable",
                action = "appropriate",
                target,
                reason = "Ocena.",
            }
        );

    [Fact]
    public async Task The_target_is_graded_from_the_recorded_offer_and_a_wrong_one_fails()
    {
        var item = Activation(Proposed(1, "SIM r hot dog"));
        var judge = Judge(new FakeGemini((HttpStatusCode.OK, Grade("wrong"))));

        Assert.Contains("<cel_propozycji>SIM r hot dog</cel_propozycji>", judge.Prompt(item));
        var result = await judge.GradeAsync(
            item,
            new JudgeBudget(1m, Price, GeminiJudge.MaxOutputTokens),
            default
        );
        Assert.Equal(("wrong", "fail"), (result.Target, result.Verdict));
    }

    [Fact]
    public async Task Without_evidence_of_the_target_the_turn_cannot_be_assessed_never_passes()
    {
        // An older transcript: a proposal was made, its offer was not recorded.
        var item = Activation(Proposed(1));
        Assert.IsType<TargetEvidence.Missing>(item.Target);
        var judge = Judge(new FakeGemini((HttpStatusCode.OK, Grade("correct"))));
        Assert.DoesNotContain("<cel_propozycji>", judge.Prompt(item));

        var result = await judge.GradeAsync(
            item,
            new JudgeBudget(1m, Price, GeminiJudge.MaxOutputTokens),
            default
        );

        Assert.Equal(
            (JudgeRubric.CannotAssess, JudgeRubric.CannotAssess),
            (result.Target, result.Verdict)
        );
        // Another failing grade still fails; compare_coupons arguments are never recorded.
        Assert.Equal(
            "fail",
            JudgeRubric.Verdict("no", "yes", "appropriate", JudgeRubric.CannotAssess)
        );
        Assert.IsType<TargetEvidence.Missing>(
            JudgeRubric.Evidence(
                new TranscriptTurn { User = "40 l", Tools = ["compare_coupons:ok"] }
            )
        );
        Assert.IsType<TargetEvidence.None>(
            JudgeRubric.Evidence(
                new TranscriptTurn { User = "§", Tools = ["search_program_rules:ok"] }
            )
        );
    }

    [Fact]
    public async Task A_recorded_target_the_judge_calls_not_applicable_is_a_judge_error()
    {
        var item = Activation(Proposed(0));
        Assert.Equal([], Assert.IsType<TargetEvidence.Recorded>(item.Target).Offers);
        var result = await Judge(new FakeGemini((HttpStatusCode.OK, Grade("not_applicable"))))
            .GradeAsync(item, new JudgeBudget(1m, Price, GeminiJudge.MaxOutputTokens), default);
        Assert.Equal(("judge_error", "invalid_format"), (result.Status, result.ErrorKind));
    }

    [Fact]
    public async Task The_cost_limit_stops_calls_and_a_rerun_spends_nothing_twice()
    {
        var good = Answer(
            new
            {
                answers_question = "yes",
                grounded = "yes",
                action = "appropriate",
                target = "not_applicable",
                reason = "Dobrze.",
            }
        );
        var items = Items();
        var judge = Judge(new FakeGemini());
        var oneCall = new JudgeBudget(
            new JudgeBudget(1m, Price, GeminiJudge.MaxOutputTokens).WorstCase(
                judge.Characters(items[0])
            ),
            Price,
            GeminiJudge.MaxOutputTokens
        );
        // The grades stay only while their transcripts exist (retention).
        Directory.CreateDirectory(Path.Combine(_directory, "transcripts"));
        foreach (var name in items.Select(i => i.Id.Split('#')[0]).Distinct())
            File.WriteAllText(Path.Combine(_directory, "transcripts", name + ".json"), "{}");
        var first = new FakeGemini([.. items.Select(_ => (HttpStatusCode.OK, good))]);

        var (results, skipped) = await JudgeRun.RunAsync(
            _directory,
            items,
            Judge(first),
            oneCall,
            default
        );

        Assert.Single(first.Sent);
        Assert.Equal(items.Count - 1, skipped);
        Assert.True(oneCall.SpentUsd <= oneCall.LimitUsd);

        var again = new FakeGemini([.. items.Select(_ => (HttpStatusCode.OK, good))]);
        var plenty = new JudgeBudget(1m, Price, GeminiJudge.MaxOutputTokens);
        (results, skipped) = await JudgeRun.RunAsync(
            _directory,
            items,
            Judge(again),
            plenty,
            default
        );

        Assert.Equal(items.Count - 1, again.Sent.Count);
        Assert.Equal(items.Count, results.Count);
        Assert.Equal(0, skipped);

        // A pruned transcript takes its grades with it on the next load.
        File.Delete(Path.Combine(_directory, "transcripts", "c00000.json"));
        Assert.DoesNotContain(JudgeRun.LoadResults(_directory), r => r.Id.StartsWith("c00000#"));
    }

    [Fact]
    public async Task A_usage_that_is_not_a_count_is_unknown_and_charged_at_worst_case()
    {
        var body = Answer(
                new
                {
                    answers_question = "yes",
                    grounded = "yes",
                    action = "appropriate",
                    target = "not_applicable",
                    reason = "Dobrze.",
                }
            )
            .Replace("\"promptTokenCount\":1000", "\"promptTokenCount\":\"1000\"");
        var judge = Judge(new FakeGemini((HttpStatusCode.OK, body)));
        var budget = new JudgeBudget(1m, Price, GeminiJudge.MaxOutputTokens);
        var item = Items()[0];

        var result = await judge.GradeAsync(item, budget, default);

        Assert.Equal("judged", result.Status);
        Assert.Null(result.InputTokens);
        // The instructions are sent too, so they count in the estimate.
        Assert.Equal(budget.WorstCase(judge.Characters(item)), result.CostUsd);
    }

    [Fact]
    public async Task The_calibration_sheet_is_blind_and_never_overwritten()
    {
        var items = Items();
        var results = items
            .Select(
                (i, n) =>
                    new JudgeResult
                    {
                        Id = i.Id,
                        Sample = i.Sample,
                        Scenario = i.Scenario,
                        Rubric = JudgeRubric.Version,
                        Model = "judge-model",
                        Status = "judged",
                        Verdict = n % 2 == 0 ? "pass" : "fail",
                        Reason = "uzasadnienie judge'a",
                    }
            )
            .ToList();

        // Another model graded the same turns: the sheet takes only the chosen model's grades.
        var other = results.Select(r => new JudgeResult
        {
            Id = r.Id,
            Sample = r.Sample,
            Scenario = r.Scenario,
            Rubric = r.Rubric,
            Model = "other-model",
            Status = "judged",
            Verdict = "fail",
            Reason = "inny model",
        });
        results = [.. results, .. other];
        Assert.Throws<InvalidOperationException>(() =>
            Calibration.Export(_directory, "r", "no-grades", items, results, size: 10)
        );
        Assert.False(File.Exists(Calibration.SheetPath(_directory, "no-grades")));

        var rows = Calibration.Export(_directory, "r", "judge-model", items, results, size: 10);

        var sheet = await File.ReadAllTextAsync(Calibration.SheetPath(_directory, "judge-model"));
        Assert.Equal(items.Count, rows);
        Assert.DoesNotContain("uzasadnienie judge'a", sheet);
        Assert.DoesNotContain("\tpass\t", sheet);
        Assert.Throws<InvalidOperationException>(() =>
            Calibration.Export(_directory, "r", "judge-model", items, results, size: 10)
        );
    }

    [Fact]
    public void Agreement_counts_only_graded_rows_and_corrects_for_chance()
    {
        static JudgeResult R(string id, string verdict, string model = "m") =>
            new()
            {
                Id = id,
                Sample = "random",
                Scenario = "s",
                Rubric = JudgeRubric.Version,
                Model = model,
                Status = "judged",
                Verdict = verdict,
            };
        var results = new[]
        {
            R("a", "pass"),
            R("b", "pass"),
            R("c", "fail"),
            R("d", "fail"),
            R("e", "pass"),
        };
        var sheet = string.Join(
            '\n',
            "id\tscenario\tquestion\tanswer\ttools\thuman_verdict\thuman_note",
            "a\ts\tq\tx\t\tpass\t",
            "b\ts\tq\tx\t\tfail\tnie odpowiada",
            "c\ts\tq\tx\t\tfail\t",
            "d\ts\tq\tx\t\tFAIL\t",
            "e\ts\tq\tx\t\t\t"
        );

        var a = Calibration.Compare(sheet, [.. results, R("b", "fail", "other")], "m");

        // e has no human grade. Human pass/fail: a / b,c,d; judge pass: a,b.
        Assert.Equal((4, 3), (a.Graded, a.Agree));
        Assert.Equal(1, a.Matrix[1, 0]);
        // observed 0.75, chance (1*2 + 3*2) / 16 = 0.5 -> kappa 0.5
        Assert.Equal(0.5, a.Kappa!.Value, 6);
    }

    [Fact]
    public void A_v2_sheet_shows_the_target_and_cannot_assess_grades_are_left_out()
    {
        static JudgeResult R(string id, string verdict) =>
            new()
            {
                Id = id,
                Sample = "random",
                Scenario = "s",
                Rubric = JudgeRubric.Version,
                Model = "m",
                Status = "judged",
                Verdict = verdict,
            };
        var sheet = string.Join(
            '\n',
            "id\tscenario\tquestion\tanswer\ttools\tproposal_target\thuman_verdict\thuman_note",
            "a\ts\tq\tx\t\tSIM r hot dog\tfail\t",
            "b\ts\tq\tx\t\tnie można ocenić\tpass\t"
        );

        var agreement = Calibration.Compare(
            sheet,
            [R("a", "fail"), R("b", JudgeRubric.CannotAssess)],
            "m"
        );

        Assert.Equal((1, 1), (agreement.Graded, agreement.Agree));
        Assert.Equal(
            "SIM r hot dog",
            JudgeRubric.Describe(new TargetEvidence.Recorded(["SIM r hot dog"]))
        );
    }

    [Fact]
    public void A_comparison_in_the_turn_makes_even_a_recorded_proposal_unassessable()
    {
        var turn = Proposed(1, "SIM r rabat");
        turn.Tools.Add("compare_coupons:ok");

        Assert.Equal(
            "compare_coupons",
            Assert.IsType<TargetEvidence.Missing>(JudgeRubric.Evidence(turn)).Tool
        );
    }

    [Fact]
    public async Task A_grade_under_another_instruction_text_is_graded_again()
    {
        var items = Items();
        Directory.CreateDirectory(JudgeRun.Folder(_directory));
        Directory.CreateDirectory(Path.Combine(_directory, "transcripts"));
        foreach (var item in items)
            await File.WriteAllTextAsync(
                Path.Combine(_directory, "transcripts", item.Id.Split('#')[0] + ".json"),
                "{}"
            );
        var stale = items
            .Select(i => new JudgeResult
            {
                Id = i.Id,
                Sample = i.Sample,
                Scenario = i.Scenario,
                Rubric = JudgeRubric.Version,
                RubricHash = "older-text",
                Model = "judge-model",
                Status = "judged",
                Verdict = "pass",
            })
            .ToList();
        await File.WriteAllTextAsync(
            JudgeRun.ResultsPath(_directory),
            JsonSerializer.Serialize(stale, JudgeRun.ReportJson)
        );
        var good = Answer(
            new
            {
                answers_question = "no",
                grounded = "not_applicable",
                action = "inappropriate",
                target = "not_applicable",
                reason = "Źle.",
            }
        );
        var fake = new FakeGemini([.. items.Select(_ => (HttpStatusCode.OK, good))]);

        var (results, _) = await JudgeRun.RunAsync(
            _directory,
            items,
            Judge(fake),
            new JudgeBudget(1m, Price, GeminiJudge.MaxOutputTokens),
            default
        );

        Assert.Equal(items.Count, fake.Sent.Count);
        Assert.Equal(items.Count, results.Count);
        Assert.All(results, r => Assert.Equal(JudgeRubric.InstructionsHash, r.RubricHash));
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
