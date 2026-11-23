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

    [Fact]
    public async Task The_cost_limit_stops_calls_and_a_rerun_spends_nothing_twice()
    {
        var good = Answer(
            new
            {
                answers_question = "yes",
                grounded = "yes",
                action = "appropriate",
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

    public void Dispose()
    {
        if (Directory.Exists(_directory))
            Directory.Delete(_directory, recursive: true);
    }
}
