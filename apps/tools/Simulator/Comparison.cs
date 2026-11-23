using System.Text;

namespace FuelClub.Simulator;

/// <summary>
/// The same conversations before and after a change (base run vs a pass): every turn is checked
/// again with today's scenario steps, so both sides are measured by the same rules. Accounts are
/// the same users, but their state moved on (coupons activated in the base run), so a difference
/// can also come from the data - the report lists the changed turns to look at.
/// </summary>
public static class Comparison
{
    public sealed record Row(string Scenario, int Turns, int BaseOk, int PassOk);

    public sealed record Change(
        string Id,
        string Scenario,
        string User,
        bool BaseOk,
        bool PassOk,
        string Base,
        string Pass
    );

    public static (List<Row> Rows, List<Change> Changes, int Skipped) Compare(
        IReadOnlyList<(string File, TranscriptFile Transcript)> baseRun,
        IReadOnlyList<(string File, TranscriptFile Transcript)> pass
    )
    {
        var after = pass.ToDictionary(p => Path.GetFileName(p.File), p => p.Transcript);
        var rows = new Dictionary<string, (int Turns, int BaseOk, int PassOk)>();
        var changes = new List<Change>();
        var skipped = 0;
        foreach (var (file, before) in baseRun)
        {
            if (
                !after.TryGetValue(Path.GetFileName(file), out var now)
                || ChatScenarios.All.FirstOrDefault(s => s.Name == before.Scenario)
                    is not { } scenario
            )
                continue;
            // Not the same case: the pass ran another scenario for this conversation.
            if (now.Scenario != before.Scenario)
            {
                skipped++;
                continue;
            }
            var turns = Math.Min(
                Math.Min(before.Turns.Count, now.Turns.Count),
                scenario.Steps.Length
            );
            for (var i = 0; i < turns; i++)
            {
                var step = scenario.Steps[i];
                var baseOk = step.Check(before.Turns[i]).All(c => c.Ok);
                var passOk = step.Check(now.Turns[i]).All(c => c.Ok);
                var row = rows.GetValueOrDefault(scenario.Name);
                rows[scenario.Name] = (
                    row.Turns + 1,
                    row.BaseOk + (baseOk ? 1 : 0),
                    row.PassOk + (passOk ? 1 : 0)
                );
                if (baseOk != passOk)
                    changes.Add(
                        new(
                            $"{Path.GetFileNameWithoutExtension(file)}#{i}",
                            scenario.Name,
                            before.Turns[i].User,
                            baseOk,
                            passOk,
                            before.Turns[i].Assistant,
                            now.Turns[i].Assistant
                        )
                    );
            }
        }
        return (
            rows.OrderBy(p => p.Key, StringComparer.Ordinal)
                .Select(p => new Row(p.Key, p.Value.Turns, p.Value.BaseOk, p.Value.PassOk))
                .ToList(),
            changes,
            skipped
        );
    }

    public static string Report(
        string runId,
        string pass,
        List<Row> rows,
        List<Change> changes,
        int skipped
    )
    {
        var text = new StringBuilder();
        text.AppendLine($"# Porównanie - {runId}: bazowy przebieg i przejście {pass}");
        text.AppendLine();
        text.AppendLine(
            "Te same rozmowy (scenariusz, konto, decyzje), obie strony sprawdzone dzisiejszymi regułami scenariuszy. Stan kont się zmienił (kupony aktywowane w bazowym przebiegu), więc różnica może też wynikać z danych. Sprawdzenia regułowe przybliżają znaczenie."
        );
        text.AppendLine();
        if (skipped > 0)
            text.AppendLine(
                $"Pominięte rozmowy z innym scenariuszem w przejściu (nie ten sam przypadek): {skipped}."
            );
        text.AppendLine("| Scenariusz | Tury | Zgodne przed | Zgodne po |");
        text.AppendLine("| --- | --- | --- | --- |");
        foreach (var r in rows)
            text.AppendLine($"| `{r.Scenario}` | {r.Turns} | {r.BaseOk} | {r.PassOk} |");
        text.AppendLine();
        text.AppendLine(
            $"- Razem: {rows.Sum(r => r.BaseOk)} → {rows.Sum(r => r.PassOk)} z {rows.Sum(r => r.Turns)} tur zgodnych; poprawione {changes.Count(c => c.PassOk)}, pogorszone {changes.Count(c => !c.PassOk)}."
        );
        if (changes.Count > 0)
        {
            text.AppendLine();
            text.AppendLine("## Zmienione tury (do 20, najpierw pogorszone)");
            text.AppendLine();
            foreach (var c in changes.OrderBy(c => c.PassOk).Take(20))
                text.AppendLine(
                    $"- `{c.Id}` ({c.Scenario}, {(c.PassOk ? "poprawiona" : "pogorszona")}) „{c.User}”: przed „{Short(c.Base)}”, po „{Short(c.Pass)}”"
                );
        }
        return text.ToString();
    }

    private static string Short(string text)
    {
        const string demo = "Asystent w trybie demo (reguły, bez AI). ";
        var t = text.StartsWith(demo, StringComparison.Ordinal) ? text[demo.Length..] : text;
        return t.Length <= 160 ? t : t[..160] + "…";
    }
}
