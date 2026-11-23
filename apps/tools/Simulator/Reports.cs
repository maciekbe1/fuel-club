using System.Text;
using System.Text.Json;

namespace FuelClub.Simulator;

/// <summary>
/// Two reports from a run's transcripts, kept apart on purpose: whether the traffic went through
/// (technical) and whether the answers were right (correctness, rule checks). A run can be
/// technically clean with wrong answers, and the demo adapter's numbers say nothing about Gemini.
/// </summary>
public static class Reports
{
    public static (string Technical, string Correctness) Write(
        Manifest manifest,
        string provider,
        string model,
        TimeSpan duration,
        int rateLimitWaits
    )
    {
        var folder = Path.Combine(Path.GetDirectoryName(manifest.Path)!, "transcripts");
        var transcripts = Directory.Exists(folder)
            ? Directory
                .EnumerateFiles(folder, "*.json")
                .Order(StringComparer.Ordinal)
                .Select(f =>
                    JsonSerializer.Deserialize<TranscriptFile>(File.ReadAllText(f), Manifest.Json)!
                )
                .ToList()
            : [];
        var turns = transcripts.SelectMany(t => t.Turns).ToList();
        var conversationOps = manifest.Operations.Where(o => o.Kind == "conversation").ToList();
        var technical = new StringBuilder();
        technical.AppendLine($"# Przebieg techniczny - {manifest.RunId}");
        technical.AppendLine();
        technical.AppendLine(
            $"Instancja: {manifest.ApiUrl}, dostawca `{provider}`, model `{model}`."
        );
        if (provider == "demo")
            technical.AppendLine(
                "Adapter demo (reguły, bez AI): czasy pokazują aplikację i panel, nie wydajność ani koszt Gemini."
            );
        technical.AppendLine();
        technical.AppendLine(
            $"- Rozmowy: {conversationOps.Count(o => o.Status == OperationStatus.Done)} zakończonych operacji z {conversationOps.Count}; nieudane: {conversationOps.Count(o => o.Status == OperationStatus.Failed)}, w toku: {conversationOps.Count(o => o.Status == OperationStatus.Pending)}."
        );
        technical.AppendLine(
            $"- Wyniki rozmów: {Group(transcripts.Select(t => t.Outcome ?? "?"))}."
        );
        technical.AppendLine(
            $"- Tury: {turns.Count}; z `done`: {turns.Count(t => t.Done)}; z błędem strumienia: {turns.Count(t => t.Error is not null)}{(turns.Any(t => t.Error is not null) ? $" ({Group(turns.Where(t => t.Error is not null).Select(t => t.Error!))})" : "")}."
        );
        technical.AppendLine(
            $"- Czas tury po stronie klienta: p50 {Percentile(turns, 0.5)} ms, p95 {Percentile(turns, 0.95)} ms."
        );
        technical.AppendLine(
            $"- Oczekiwania na 429 (ten proces): {rateLimitWaits}. Czas tego uruchomienia: {duration.TotalMinutes:F1} min."
        );
        technical.AppendLine();
        technical.AppendLine(
            "Liczby rozmów, tur i wywołań narzędzi oraz koszt - w panelu (ruch syntetyczny, filtr RunId)."
        );

        var correctness = new StringBuilder();
        correctness.AppendLine($"# Poprawność odpowiedzi - {manifest.RunId}");
        correctness.AppendLine();
        correctness.AppendLine(
            $"Dostawca `{provider}`, model `{model}`. Sprawdzenia regułowe (narzędzia, wzorce, propozycja konsultanta) przybliżają znaczenie; nie zastępują oceny człowieka ani skalibrowanego judge'a."
        );
        correctness.AppendLine();
        correctness.AppendLine(
            "| Scenariusz | Regresja | Tury | Tury zgodne | Sprawdzenia niezgodne (przykład) |"
        );
        correctness.AppendLine("| --- | --- | --- | --- | --- |");
        foreach (
            var group in transcripts
                .Where(t => t.Turns.Count > 0)
                .GroupBy(t => t.Scenario)
                .OrderBy(g => g.Key, StringComparer.Ordinal)
        )
        {
            var scenarioTurns = group.SelectMany(t => t.Turns).ToList();
            var passed = scenarioTurns.Count(t => t.Checks.All(c => c.Ok));
            var failed = scenarioTurns
                .SelectMany(t =>
                    t.Checks.Where(c => !c.Ok).Select(c => $"{c.Name} (\"{Short(t.User)}\")")
                )
                .Distinct()
                .Take(2);
            correctness.AppendLine(
                $"| `{group.Key}` | {(group.First().Regression ? "tak" : "")} | {scenarioTurns.Count} | {passed} | {string.Join("; ", failed)} |"
            );
        }
        var regressionTurns = transcripts
            .Where(t => t.Regression)
            .SelectMany(t => t.Turns)
            .ToList();
        var otherTurns = transcripts.Where(t => !t.Regression).SelectMany(t => t.Turns).ToList();
        correctness.AppendLine();
        correctness.AppendLine(
            $"- Scenariusze zwykłe: {otherTurns.Count(t => t.Checks.All(c => c.Ok))} z {otherTurns.Count} tur zgodnych."
        );
        correctness.AppendLine(
            $"- Scenariusze regresyjne (znane pomyłki): {regressionTurns.Count(t => t.Checks.All(c => c.Ok))} z {regressionTurns.Count} tur zgodnych. Przeformułowane scenariusze zwykłe generują ruch; nie oznaczają, że agent przestał się mylić."
        );

        var root = Path.GetDirectoryName(manifest.Path)!;
        var technicalPath = Path.Combine(root, "report-technical.md");
        var correctnessPath = Path.Combine(root, "report-correctness.md");
        File.WriteAllText(technicalPath, technical.ToString());
        File.WriteAllText(correctnessPath, correctness.ToString());
        return (technicalPath, correctnessPath);
    }

    private static string Group(IEnumerable<string> values) =>
        string.Join(
            ", ",
            values
                .GroupBy(v => v)
                .OrderByDescending(g => g.Count())
                .ThenBy(g => g.Key, StringComparer.Ordinal)
                .Select(g => $"{g.Key} {g.Count()}")
        );

    private static string Percentile(List<TranscriptTurn> turns, double p)
    {
        var values = turns.Select(t => t.ClientMs).Order().ToList();
        if (values.Count == 0)
            return "-";
        return values[(int)Math.Min(values.Count - 1, Math.Ceiling(p * values.Count) - 1)]
            .ToString();
    }

    private static string Short(string text) => text.Length <= 40 ? text : text[..40] + "…";

    /// <summary>The transcript as written (Turns has no setter on the live type).</summary>
    private sealed class TranscriptFile
    {
        public string Scenario { get; set; } = "";
        public bool Regression { get; set; }
        public string? Outcome { get; set; }
        public List<TranscriptTurn> Turns { get; set; } = [];
    }
}
