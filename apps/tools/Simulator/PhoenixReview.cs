using System.Diagnostics;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace FuelClub.Simulator;

/// <summary>
/// Human calibration in a local Phoenix (ADR 0019): the calibration rows go out as spans with
/// the question and the answer (synthetic turns only, never the API's own spans), the human
/// grades them in the UI and agreement reads the grades back. The judge's verdicts are sent only
/// after every row is graded, so they cannot steer the human.
/// </summary>
public static class PhoenixReview
{
    public const string Project = "fuelclub-ocena";
    public const string HumanAnnotation = "human_verdict";
    public const string JudgeAnnotation = "judge_verdict";
    private const string SourceName = "FuelClub.Simulator.Calibration";

    public sealed record Exported(string Id, string SpanId, string TraceId);

    public sealed record ExportFile(
        string Run,
        string Project,
        string Rubric,
        string Model,
        List<Exported> Spans
    )
    {
        public bool JudgeSent { get; init; }

        /// <summary>The human grades when the judge's verdicts went out; later edits do not count.</summary>
        public List<HumanGrade>? Frozen { get; init; }
    }

    public sealed record HumanGrade(string SpanId, string Label, string? Note);

    public static string MapPath(string runFolder, string model) =>
        Path.Combine(JudgeRun.Folder(runFolder), $"phoenix-{JudgeRubric.Version}-{model}.json");

    /// <summary>Only a Phoenix on this machine: the spans carry the turns' text.</summary>
    public static Uri Loopback(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || !uri.IsLoopback)
            throw new ArgumentException($"--phoenix must be a loopback URL, not '{url}'.");
        var builder = new UriBuilder(uri) { Query = "", Fragment = "" };
        if (!builder.Path.EndsWith('/'))
            builder.Path += "/";
        return builder.Uri;
    }

    /// <summary>What the human reads: the earlier turns, then the graded question.</summary>
    public static string Input(JudgeItem item)
    {
        var text = new StringBuilder();
        if (item.Before.Count > 0)
        {
            text.AppendLine("Wcześniej:");
            foreach (var (user, assistant) in item.Before)
                text.AppendLine($"Klient: {user}").AppendLine($"Asystent: {assistant}");
            text.AppendLine();
        }
        return text.Append($"Klient: {item.User}").ToString();
    }

    public static IReadOnlyList<KeyValuePair<string, object?>> Attributes(
        JudgeItem item,
        string run
    )
    {
        var metadata = new JsonObject
        {
            ["run"] = run,
            ["item_id"] = item.Id,
            ["scenario"] = item.Scenario,
            ["tools"] = string.Join(", ", item.Tools),
            ["proposal_target"] = JudgeRubric.Describe(item.Target),
            ["handoff_button"] = item.Handoff,
        };
        return
        [
            new("openinference.span.kind", "CHAIN"),
            new("input.value", Input(item)),
            new("input.mime_type", "text/plain"),
            new("output.value", item.Assistant),
            new("output.mime_type", "text/plain"),
            new("metadata", metadata.ToJsonString()),
            new("fuelclub.item_id", item.Id),
            new("fuelclub.scenario", item.Scenario),
        ];
    }

    public static async Task<ExportFile> ExportAsync(
        Uri phoenix,
        string run,
        string model,
        IReadOnlyList<JudgeItem> rows,
        HttpClient http,
        CancellationToken ct
    )
    {
        await EnsureConfigAsync(http, ct);
        var spans = new List<Exported>();
        using (var source = new ActivitySource(SourceName))
        using (
            var provider = Sdk.CreateTracerProviderBuilder()
                .AddSource(SourceName)
                .SetSampler(new AlwaysOnSampler())
                .SetResourceBuilder(
                    ResourceBuilder
                        .CreateEmpty()
                        .AddService("fuelclub-simulator")
                        .AddAttributes([new("openinference.project.name", Project)])
                )
                .AddOtlpExporter(o =>
                {
                    o.Endpoint = new Uri(phoenix, "v1/traces");
                    o.Protocol = OtlpExportProtocol.HttpProtobuf;
                    o.Headers = $"x-project-name={Project}";
                    o.ExportProcessorType = ExportProcessorType.Simple;
                })
                .Build()
        )
        {
            foreach (var item in rows)
            {
                // A root span each: one row, one trace, nothing inherited from the caller.
                Activity.Current = null;
                using var activity = source.StartActivity(
                    $"ocena {item.Id}",
                    ActivityKind.Internal,
                    default(ActivityContext),
                    Attributes(item, run)
                );
                if (activity is null)
                    throw new InvalidOperationException("The calibration span was not sampled.");
                spans.Add(
                    new(item.Id, activity.SpanId.ToHexString(), activity.TraceId.ToHexString())
                );
            }
            if (!provider.ForceFlush(10_000))
                throw new InvalidOperationException(
                    $"Phoenix at {phoenix} did not take the spans."
                );
        }
        // A failed OTLP export is only logged: check Phoenix has every span before the map.
        await VerifySpansAsync(http, spans, ct);
        await AssignConfigAsync(http, ct);
        return new ExportFile(run, Project, JudgeRubric.Version, model, spans);
    }

    private static async Task VerifySpansAsync(
        HttpClient http,
        IReadOnlyList<Exported> spans,
        CancellationToken ct
    )
    {
        var wanted = spans.Select(s => s.SpanId).ToHashSet();
        for (var attempt = 0; ; attempt++)
        {
            var found = new HashSet<string>();
            string? cursor = null;
            do
            {
                using var response = await http.GetAsync(
                    $"v1/projects/{Project}/spans?limit=1000"
                        + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}"),
                    ct
                );
                // The project appears with its first ingested span.
                if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
                    break;
                response.EnsureSuccessStatusCode();
                var page = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
                foreach (var span in page["data"]!.AsArray())
                    if (span?["context"]?["span_id"]?.GetValue<string>() is { } id)
                        found.Add(id);
                cursor = NextCursor(page);
            } while (cursor is not null);
            if (wanted.IsSubsetOf(found))
                return;
            if (attempt == 10)
                throw new InvalidOperationException(
                    $"Phoenix has {wanted.Count(found.Contains)} of {wanted.Count} exported spans."
                );
            await Task.Delay(500, ct);
        }
    }

    private static string? NextCursor(JsonNode page) =>
        page["next_cursor"] is JsonValue next && next.GetValueKind() == JsonValueKind.String
            ? next.GetValue<string>()
            : null;

    private static async Task EnsureConfigAsync(HttpClient http, CancellationToken ct)
    {
        using var list = await http.GetAsync("v1/annotation_configs?limit=1000", ct);
        list.EnsureSuccessStatusCode();
        var body = await list.Content.ReadFromJsonAsync<JsonObject>(ct);
        if (
            body?["data"] is JsonArray configs
            && configs.Any(c => c?["name"]?.GetValue<string>() == HumanAnnotation)
        )
            return;
        using var created = await http.PostAsJsonAsync(
            "v1/annotation_configs",
            new
            {
                name = HumanAnnotation,
                type = "CATEGORICAL",
                description = "Ocena człowieka tury symulacji (pass/fail); komentarz w polu wyjaśnienia.",
                optimization_direction = "MAXIMIZE",
                values = new[]
                {
                    new { label = "pass", score = 1 },
                    new { label = "fail", score = 0 },
                },
            },
            ct
        );
        created.EnsureSuccessStatusCode();
    }

    /// <summary>The project exists once its first span is in; ingestion is asynchronous.</summary>
    private static async Task AssignConfigAsync(HttpClient http, CancellationToken ct)
    {
        for (var attempt = 0; ; attempt++)
        {
            using var response = await http.PutAsync(
                $"v1/projects/{Project}/annotation_configs/{HumanAnnotation}",
                null,
                ct
            );
            if (response.IsSuccessStatusCode)
                return;
            if (response.StatusCode != System.Net.HttpStatusCode.NotFound || attempt == 10)
                throw new InvalidOperationException(
                    $"Phoenix did not assign {HumanAnnotation} to {Project}: {(int)response.StatusCode}."
                );
            await Task.Delay(500, ct);
        }
    }

    public static async Task<List<HumanGrade>> HumanGradesAsync(
        ExportFile file,
        HttpClient http,
        CancellationToken ct
    )
    {
        // Every page first: "the latest per span" must see all annotations of a span.
        var all = new JsonArray();
        foreach (var chunk in file.Spans.Chunk(50))
        {
            string? cursor = null;
            do
            {
                var query =
                    string.Join("&", chunk.Select(s => $"span_ids={s.SpanId}"))
                    + $"&include_annotation_names={HumanAnnotation}&limit=1000"
                    + (cursor is null ? "" : $"&cursor={Uri.EscapeDataString(cursor)}");
                using var response = await http.GetAsync(
                    $"v1/projects/{file.Project}/span_annotations?{query}",
                    ct
                );
                response.EnsureSuccessStatusCode();
                var page = JsonNode.Parse(await response.Content.ReadAsStringAsync(ct))!;
                foreach (var annotation in page["data"]!.AsArray())
                    all.Add(annotation?.DeepClone());
                cursor = NextCursor(page);
            } while (cursor is not null);
        }
        using var document = JsonDocument.Parse(new JsonObject { ["data"] = all }.ToJsonString());
        return ParseHuman(document.RootElement).ToList();
    }

    /// <summary>HUMAN pass/fail only; with several, the latest one per span.</summary>
    public static IEnumerable<HumanGrade> ParseHuman(JsonElement body) =>
        body.GetProperty("data")
            .EnumerateArray()
            .Where(a =>
                a.GetProperty("name").GetString() == HumanAnnotation
                && a.GetProperty("annotator_kind").GetString() == "HUMAN"
            )
            .Select(a =>
            {
                var result = a.TryGetProperty("result", out var r) ? r : default;
                string? Field(string name) =>
                    result.ValueKind == JsonValueKind.Object
                    && result.TryGetProperty(name, out var v)
                    && v.ValueKind == JsonValueKind.String
                        ? v.GetString()
                        : null;
                var updated = a.TryGetProperty("updated_at", out var u) ? u.GetString() : null;
                return (
                    Grade: new HumanGrade(
                        a.GetProperty("span_id").GetString()!,
                        (Field("label") ?? "").Trim().ToLowerInvariant(),
                        Field("explanation")
                    ),
                    Updated: updated ?? ""
                );
            })
            .Where(g => g.Grade.Label is "pass" or "fail")
            .GroupBy(g => g.Grade.SpanId)
            .Select(g => g.OrderBy(x => x.Updated, StringComparer.Ordinal).Last().Grade);

    /// <summary>Writes the grades into the sheet, so the evidence stays next to the run.</summary>
    public static string Fill(string sheet, ExportFile file, IEnumerable<HumanGrade> grades)
    {
        var byItem = grades
            .Join(file.Spans, g => g.SpanId, s => s.SpanId, (g, s) => (s.Id, g))
            .ToDictionary(x => x.Id, x => x.g);
        var exported = file.Spans.Select(s => s.Id).ToHashSet();
        var lines = sheet.Split('\n');
        var header = lines[0].TrimEnd('\r').Split('\t');
        var verdict = Array.IndexOf(header, "human_verdict");
        var note = Array.IndexOf(header, "human_note");
        if (verdict < 0 || note < 0)
            throw new InvalidDataException("The sheet has no human_verdict / human_note column.");
        for (var i = 1; i < lines.Length; i++)
        {
            var cells = lines[i].Split('\t');
            if (cells.Length <= Math.Max(verdict, note) || !exported.Contains(cells[0]))
                continue;
            // A grade removed in Phoenix goes from the sheet too: the sheet mirrors Phoenix.
            var g = byItem.GetValueOrDefault(cells[0]);
            cells[verdict] = g?.Label ?? "";
            cells[note] = (g?.Note ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ');
            lines[i] = string.Join('\t', cells);
        }
        return string.Join('\n', lines);
    }

    /// <summary>After the human: the judge's verdict and reason on the same spans, to compare.</summary>
    public static async Task SendJudgeAsync(
        ExportFile file,
        IReadOnlyList<JudgeResult> results,
        HttpClient http,
        CancellationToken ct
    )
    {
        var judged = JudgeRun
            .Of(results, file.Model)
            .Where(r => r.Status == "judged")
            .ToDictionary(r => r.Id);
        var data = file
            .Spans.Where(s => judged.ContainsKey(s.Id))
            .Select(s => new
            {
                span_id = s.SpanId,
                name = JudgeAnnotation,
                annotator_kind = "LLM",
                identifier = file.Rubric,
                result = new { label = judged[s.Id].Verdict, explanation = judged[s.Id].Reason },
                metadata = new { rubric = file.Rubric, model = file.Model },
            })
            .ToList();
        using var response = await http.PostAsJsonAsync(
            "v1/span_annotations?sync=true",
            new { data },
            ct
        );
        response.EnsureSuccessStatusCode();
    }
}
