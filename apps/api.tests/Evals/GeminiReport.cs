using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Text.Json;
using FuelClub.Api.Modules.Assistant.Model;

namespace FuelClub.Api.Tests.Evals;

/// <summary>Runs on Gemini only when asked: FUELCLUB_GEMINI_EVALS=pilot|full and a key (GeminiFact).</summary>
public sealed class GeminiEvalFactAttribute : FactAttribute
{
    public static string? Mode { get; } =
        Environment.GetEnvironmentVariable("FUELCLUB_GEMINI_EVALS") is "pilot" or "full"
            ? Environment.GetEnvironmentVariable("FUELCLUB_GEMINI_EVALS")
            : null;

    public GeminiEvalFactAttribute()
    {
        if (Mode is null)
            Skip = "Set FUELCLUB_GEMINI_EVALS=pilot or full to run the assistant cases on Gemini.";
        else if (string.IsNullOrWhiteSpace(GeminiFactAttribute.Key))
            Skip = new GeminiFactAttribute().Skip;
    }
}

/// <summary>Markdown for people and JSON for comparison, in apps/api.tests/Evals/reports.</summary>
public static class GeminiReport
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    /// <summary>
    /// USD per 1M tokens, standard paid tier, gemini-3.5-flash-lite (ai.google.dev/gemini-api/docs/pricing,
    /// read 2026-11-23; output includes thinking). Another model: override with
    /// FUELCLUB_GEMINI_PRICE_INPUT / FUELCLUB_GEMINI_PRICE_OUTPUT.
    /// </summary>
    private static decimal InputPrice => Price("FUELCLUB_GEMINI_PRICE_INPUT", 0.30m);

    private static decimal OutputPrice => Price("FUELCLUB_GEMINI_PRICE_OUTPUT", 2.50m);

    private static decimal Price(string variable, decimal fallback) =>
        decimal.TryParse(
            Environment.GetEnvironmentVariable(variable),
            NumberStyles.Number,
            CultureInfo.InvariantCulture,
            out var value
        )
            ? value
            : fallback;

    private static string Cost(long input, long output) =>
        Math.Round((input * InputPrice + output * OutputPrice) / 1_000_000m, 4)
            .ToString(CultureInfo.InvariantCulture);

    private static readonly string[] Kinds =
    [
        "tools",
        "arguments",
        "answer",
        "safety",
        "turn",
        "trace",
        "budget",
    ];

    public static string Write(
        string mode,
        string suiteVersion,
        string model,
        List<AssistantEvals.CaseReport> runs,
        bool stopped,
        int budget,
        int fullRunCases
    )
    {
        var directory = Path.Combine(RepositoryRoot(), "apps", "api.tests", "Evals", "reports");
        Directory.CreateDirectory(directory);
        var stamp = DateTimeOffset.UtcNow.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
        var path = Path.Combine(directory, $"gemini-{mode}-{stamp}.md");

        var input = runs.Sum(r => r.InputTokens ?? 0);
        var output = runs.Sum(r => r.OutputTokens ?? 0);
        var perCaseIn = runs.Count == 0 ? 0 : input / runs.Count;
        var perCaseOut = runs.Count == 0 ? 0 : output / runs.Count;
        var commit = Commit();
        var summary = Kinds
            .Select(kind =>
            {
                var checks = runs.SelectMany(r => r.Checks).Where(c => c.Kind == kind).ToList();
                return new
                {
                    kind,
                    passed = checks.Count(c => c.Ok),
                    total = checks.Count,
                };
            })
            .Where(k => k.total > 0)
            .ToList();
        var casesPassed = runs.Count(r => r.Checks.All(c => c.Ok));

        File.WriteAllText(
            Path.ChangeExtension(path, ".json"),
            JsonSerializer.Serialize(
                new
                {
                    mode,
                    suiteVersion,
                    model,
                    promptVersion = GeminiChatModel.Version,
                    commit,
                    createdAt = DateTimeOffset.UtcNow,
                    stoppedByBudget = stopped,
                    tokenBudget = budget,
                    tokens = new { input, output },
                    costUsd = Cost(input, output),
                    prices = new { input = InputPrice, output = OutputPrice },
                    summary,
                    runs,
                },
                Json
            )
        );

        var md = new StringBuilder();
        md.AppendLine($"# Evale Gemini - {mode} ({stamp} UTC)");
        md.AppendLine();
        md.AppendLine(
            $"- zestaw: `{suiteVersion}`, model: `{model}`, prompt: `{GeminiChatModel.Version}`, commit: `{commit}`"
        );
        md.AppendLine(
            $"- przebiegi: {runs.Count}, w pełni zaliczone: {casesPassed}{(stopped ? " (przerwane: budżet tokenów)" : "")}"
        );
        md.AppendLine(
            $"- tokeny: wejście {input}, wyjście {output} (na przypadek średnio {perCaseIn} / {perCaseOut})"
        );
        md.AppendLine(
            $"- koszt: ~{Cost(input, output)} USD (ceny we/wy za 1M tokenów: {InputPrice.ToString(CultureInfo.InvariantCulture)} / {OutputPrice.ToString(CultureInfo.InvariantCulture)} USD)"
        );
        md.AppendLine(
            $"- szacunek pełnego przebiegu ({fullRunCases} przypadków = przypadki bez modelu skryptowego × 3): wejście ~{perCaseIn * fullRunCases}, wyjście ~{perCaseOut * fullRunCases}, koszt ~{Cost((long)perCaseIn * fullRunCases, (long)perCaseOut * fullRunCases)} USD"
        );
        md.AppendLine();
        md.AppendLine("| Rodzaj | Zaliczone |");
        md.AppendLine("| --- | --- |");
        foreach (var k in summary)
            md.AppendLine($"| {k.kind} | {k.passed}/{k.total} |");
        md.AppendLine();
        md.AppendLine("| Przypadek | Wynik | Narzędzia | Tokeny (we/wy) | ms | Niezaliczone |");
        md.AppendLine("| --- | --- | --- | --- | --- | --- |");
        foreach (var r in runs)
        {
            var failed = r
                .Checks.Where(c => !c.Ok)
                .Select(c => $"{c.Kind}/{c.Name}: {c.Detail}".Replace("|", "/").Replace("\n", " "));
            md.AppendLine(
                $"| {r.Id} | {r.Outcome} | {string.Join(", ", r.Tools)} | {r.InputTokens}/{r.OutputTokens} | {r.TotalMs} | {string.Join("; ", failed)} |"
            );
        }
        File.WriteAllText(path, md.ToString());
        return path;
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (
            directory is not null && !File.Exists(Path.Combine(directory.FullName, "FuelClub.slnx"))
        )
            directory = directory.Parent;
        return directory?.FullName
            ?? throw new InvalidOperationException("Repository root not found.");
    }

    private static string Commit()
    {
        try
        {
            using var git = Process.Start(
                new ProcessStartInfo("git", "rev-parse --short HEAD")
                {
                    RedirectStandardOutput = true,
                    WorkingDirectory = RepositoryRoot(),
                }
            )!;
            var sha = git.StandardOutput.ReadToEnd().Trim();
            git.WaitForExit();
            return sha;
        }
        catch (Exception)
        {
            return "unknown";
        }
    }
}
