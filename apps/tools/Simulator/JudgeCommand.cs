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
                // Phoenix: the URL is checked first, and an existing sheet without a Phoenix
                // export is reused (a failed export can be retried; a TSV-only run can move).
                var phoenix = options.Get("phoenix") is { } url
                    ? PhoenixReview.Loopback(url)
                    : null;
                if (phoenix is not null && File.Exists(Calibration.SheetPath(runFolder, model)))
                    return await ToPhoenixAsync(phoenix, run, runFolder, model, all, ct);
                var rows = Calibration.Export(
                    runFolder,
                    run,
                    model,
                    all,
                    results,
                    options.Int("size", 30)
                );
                if (phoenix is not null)
                    return await ToPhoenixAsync(phoenix, run, runFolder, model, all, ct);
                Console.WriteLine(
                    $"{rows} rows: {Calibration.SheetPath(runFolder, model)} - fill human_verdict (pass/fail), then run agreement."
                );
                return 0;
            }
            catch (Exception e)
                when (e is InvalidOperationException or ArgumentException or HttpRequestException)
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
        var text = await File.ReadAllTextAsync(sheet, ct);
        var pending = "";
        if (options.Get("phoenix") is { } phoenixUrl)
            try
            {
                pending = await FromPhoenixAsync(phoenixUrl, runFolder, model, sheet, results, ct);
                text = await File.ReadAllTextAsync(sheet, ct);
            }
            catch (Exception e)
                when (e is InvalidOperationException or ArgumentException or HttpRequestException)
            {
                Console.WriteLine(e.Message);
                return 2;
            }
        var result = Calibration.Compare(text, results, model);
        var report = Path.Combine(JudgeRun.Folder(runFolder), $"report-calibration-{model}.md");
        await File.WriteAllTextAsync(report, AgreementReport(run, model, result), ct);
        Console.WriteLine($"graded {result.Graded}, agree {result.Agree}{pending}: {report}");
        return 0;
    }

    /// <summary>Human grades from Phoenix into the sheet; the judge's verdicts after the last one.</summary>
    private static async Task<string> FromPhoenixAsync(
        string phoenixUrl,
        string runFolder,
        string model,
        string sheet,
        IReadOnlyList<JudgeResult> results,
        CancellationToken ct
    )
    {
        var text = await File.ReadAllTextAsync(sheet, ct);
        {
            var map = PhoenixReview.MapPath(runFolder, model);
            if (!File.Exists(map))
                throw new InvalidOperationException(
                    $"No Phoenix export for {model}: run calibrate --phoenix first."
                );
            var file = JsonSerializer.Deserialize<PhoenixReview.ExportFile>(
                await File.ReadAllTextAsync(map, ct),
                Manifest.Json
            )!;
            using var http = new HttpClient { BaseAddress = PhoenixReview.Loopback(phoenixUrl) };
            var grades = await PhoenixReview.HumanGradesAsync(file, http, ct);
            var note = "";
            // After the judge's verdicts were shown, edits would inflate agreement: the grades
            // from that moment count, a later change is only reported.
            if (file.Frozen is { } frozen)
            {
                var changed = grades.Except(frozen).Count() + frozen.Except(grades).Count();
                note =
                    changed > 0
                        ? $"; {changed} grade change(s) after the judge's verdicts were shown - not counted"
                        : "";
                grades = frozen;
            }
            await File.WriteAllTextAsync(sheet, PhoenixReview.Fill(text, file, grades), ct);
            var graded = grades.Select(g => g.SpanId).Distinct().Count();
            if (graded < file.Spans.Count)
                return $"; {file.Spans.Count - graded} of {file.Spans.Count} rows still ungraded in Phoenix";
            // Blind until the last row: the judge's verdicts go to Phoenix only then.
            if (!file.JudgeSent)
            {
                await PhoenixReview.SendJudgeAsync(file, results, http, ct);
                await File.WriteAllTextAsync(
                    map,
                    JsonSerializer.Serialize(
                        file with
                        {
                            JudgeSent = true,
                            Frozen = grades,
                        },
                        Manifest.Json
                    ),
                    ct
                );
            }
            return "; judge verdicts in Phoenix" + note;
        }
    }

    private static async Task<int> ToPhoenixAsync(
        Uri phoenix,
        string run,
        string runFolder,
        string model,
        IReadOnlyList<JudgeItem> all,
        CancellationToken ct
    )
    {
        var map = PhoenixReview.MapPath(runFolder, model);
        if (File.Exists(map))
        {
            Console.WriteLine($"{map} exists: the rows are already in Phoenix.");
            return 2;
        }
        var ids = (await File.ReadAllLinesAsync(Calibration.SheetPath(runFolder, model), ct))
            .Skip(1)
            .Where(l => l.Length > 0)
            .Select(l => l.Split('\t')[0])
            .ToList();
        var byId = all.ToDictionary(i => i.Id);
        var rows = ids.Select(id => byId[id]).ToList();
        using var http = new HttpClient { BaseAddress = phoenix };
        var file = await PhoenixReview.ExportAsync(phoenix, run, model, rows, http, ct);
        await File.WriteAllTextAsync(map, JsonSerializer.Serialize(file, Manifest.Json), ct);
        Console.WriteLine(
            $"{rows.Count} rows in Phoenix project {PhoenixReview.Project} ({http.BaseAddress}): grade {PhoenixReview.HumanAnnotation} (pass/fail, note in the explanation), then run agreement --phoenix."
        );
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
