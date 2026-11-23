using System.Globalization;
using System.Text.Json;

namespace FuelClub.Simulator;

/// <summary>judge, calibrate and agreement on a run's transcripts (local files, no API calls).</summary>
internal static class JudgeCommand
{
    private const string ApiUserSecretsId = "0f35ea41-6338-4a42-8230-460fc2432078";

    public static async Task<int> RunAsync(
        string command,
        string run,
        string manifestPath,
        Options options,
        CancellationToken ct
    )
    {
        // Only the simulator's own runs: synthetic transcripts, never real users' traffic.
        if (!File.Exists(manifestPath) || Manifest.Load(manifestPath).RunId != run)
        {
            Console.WriteLine($"Refused: no simulator manifest for run '{run}'.");
            return 2;
        }
        // A pass (converse --pass) has its own transcripts and judge folder; turn ids match the
        // base run's, so the same turns are graded and compared.
        var runFolder = options.Get("pass") is { } pass
            ? Path.Combine(Path.GetDirectoryName(manifestPath)!, "passes", pass)
            : Path.GetDirectoryName(manifestPath)!;
        var model = options.Get("model") ?? "gemini-3.5-flash-lite";
        if (command is "calibrate" or "agreement")
            return await CalibrationAsync(command, run, runFolder, model, options, ct);
        var items = JudgeSampling.Select(
            run,
            TranscriptFile.LoadRun(runFolder),
            options.Int("random", 40),
            options.Int("errors", 20),
            options.Int("regressions", 10)
        );

        var key = ApiKey();
        if (key is null)
        {
            Console.WriteLine(
                "No Gemini key: set GEMINI_API_KEY or the API's user-secret Assistant:Gemini:ApiKey."
            );
            return 2;
        }
        var price = new JudgePrice(
            options.Get("price-version") ?? "gemini-3.5-flash-lite-standard-2026-11-23",
            Decimal(options, "price-in", 0.30m),
            Decimal(options, "price-out", 2.50m)
        );
        var budget = new JudgeBudget(
            Decimal(options, "max-usd", 0.20m),
            price,
            GeminiJudge.MaxOutputTokens
        );
        var rules = await File.ReadAllTextAsync(
            options.Get("rules") ?? "apps/api/Modules/Knowledge/Documents/regulamin-v1.md",
            ct
        );
        using var http = new HttpClient
        {
            BaseAddress = new Uri("https://generativelanguage.googleapis.com/v1beta/"),
        };
        var judge = new GeminiJudge(http, key, model, rules);
        var (results, skipped) = await JudgeRun.RunAsync(runFolder, items, judge, budget, ct);
        // With the grades: the reasons may quote answers, so it is pruned with them.
        var path = Path.Combine(JudgeRun.Folder(runFolder), "report-judge.md");
        await File.WriteAllTextAsync(
            path,
            JudgeRun.Report(run, results, items, skipped, budget, price, model),
            ct
        );
        var mine = JudgeRun.Of(results, model).Where(r => items.Any(i => i.Id == r.Id)).ToList();
        Console.WriteLine(
            $"{items.Count} items: graded {mine.Count(r => r.Status == "judged")}, judge errors {mine.Count(r => r.Status == "judge_error")}, skipped by the cost limit {skipped}; this run {budget.SpentUsd.ToString("0.0000", CultureInfo.InvariantCulture)} USD: {path}"
        );
        return 0;
    }

    private static async Task<int> CalibrationAsync(
        string command,
        string run,
        string runFolder,
        string model,
        Options options,
        CancellationToken ct
    )
    {
        var results = JudgeRun.LoadResults(runFolder);
        if (command == "calibrate")
        {
            // Every turn's text, so the sheet can draw from all the model's grades.
            var all = JudgeSampling.Select(
                run,
                TranscriptFile.LoadRun(runFolder),
                int.MaxValue,
                0,
                int.MaxValue
            );
            try
            {
                var rows = Calibration.Export(
                    runFolder,
                    run,
                    model,
                    all,
                    results,
                    options.Int("size", 30)
                );
                Console.WriteLine(
                    $"{rows} rows: {Calibration.SheetPath(runFolder, model)} - fill human_verdict (pass/fail), then run agreement."
                );
                return 0;
            }
            catch (InvalidOperationException e)
            {
                Console.WriteLine(e.Message);
                return 2;
            }
        }
        var sheet = Calibration.SheetPath(runFolder, model);
        if (!File.Exists(sheet))
        {
            Console.WriteLine($"No calibration sheet for {model}: run calibrate first.");
            return 2;
        }
        var result = Calibration.Compare(await File.ReadAllTextAsync(sheet, ct), results, model);
        var report = Path.Combine(JudgeRun.Folder(runFolder), $"report-calibration-{model}.md");
        await File.WriteAllTextAsync(report, AgreementReport(run, model, result), ct);
        Console.WriteLine($"graded {result.Graded}, agree {result.Agree}: {report}");
        return 0;
    }

    private static string? ApiKey()
    {
        if (Environment.GetEnvironmentVariable("GEMINI_API_KEY") is { Length: > 0 } fromEnv)
            return fromEnv;
        var secrets = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".microsoft",
            "usersecrets",
            ApiUserSecretsId,
            "secrets.json"
        );
        if (!File.Exists(secrets))
            return null;
        using var document = JsonDocument.Parse(File.ReadAllText(secrets));
        return document.RootElement.TryGetProperty("Assistant:Gemini:ApiKey", out var value)
            ? value.GetString()
            : null;
    }

    private static decimal Decimal(Options options, string name, decimal fallback) =>
        options.Get(name) is { } text
            ? decimal.Parse(text, NumberStyles.Number, CultureInfo.InvariantCulture)
            : fallback;

    public static string AgreementReport(string run, string model, Calibration.Agreement a)
    {
        var kappa = a.Kappa is { } k
            ? k.ToString("0.00", CultureInfo.InvariantCulture)
            : "nieokreślona";
        return $"""
            # Kalibracja judge'a - {run}

            Rubryka `{JudgeRubric.Version}`, model oceniający `{model}`. Ocenione przez człowieka i judge'a: {a.Graded}.

            - Zgodność: {a.Agree} z {a.Graded}; kappa Cohena: {kappa} (poprawka na zgodność przypadkową).
            - Człowiek pass / judge pass: {a.Matrix[0, 0]}; człowiek pass / judge fail: {a.Matrix[
                0,
                1
            ]}.
            - Człowiek fail / judge pass: {a.Matrix[1, 0]}; człowiek fail / judge fail: {a.Matrix[
                1,
                1
            ]}.

            Mała próbka daje szeroki przedział niepewności. Judge fail przy człowieku pass zawyża błędy agenta; judge pass przy człowieku fail je ukrywa.
            """;
    }
}
