using System.Text.Json;
using FuelClub.Simulator;

namespace FuelClub.Api.Tests;

/// <summary>Human calibration in Phoenix (ADR 0019): loopback only, span content, grades back.</summary>
public sealed class PhoenixReviewTests
{
    private static JudgeItem Item(string id) =>
        new(
            id,
            "random",
            "aktywacja",
            false,
            true,
            [("Cześć", "Dzień dobry")],
            "Aktywuj kupon rabat",
            "Proponuję aktywację kuponu rabat.",
            ["list_my_coupons:ok", "propose_coupon_activation:ok"],
            false,
            new TargetEvidence.Recorded(["SIM rabat"])
        );

    private static readonly PhoenixReview.ExportFile File = new(
        "gemini-1",
        PhoenixReview.Project,
        JudgeRubric.Version,
        "m",
        [new("c1#0", "aaaa", "t1"), new("c2#0", "bbbb", "t2")]
    );

    [Theory]
    [InlineData("http://127.0.0.1:6006")]
    [InlineData("http://localhost:6006/")]
    [InlineData("http://[::1]:6006")]
    public void Loopback_accepts_this_machine(string url) =>
        Assert.EndsWith("/", PhoenixReview.Loopback(url).AbsoluteUri);

    [Fact]
    public void Loopback_drops_query_and_fragment() =>
        Assert.Equal(
            "http://localhost:6006/v1/traces",
            new Uri(PhoenixReview.Loopback("http://localhost:6006?x=1#y"), "v1/traces").AbsoluteUri
        );

    [Theory]
    [InlineData("http://phoenix.example.com:6006")]
    [InlineData("http://10.0.0.5:6006")]
    [InlineData("not a url")]
    public void Loopback_refuses_other_hosts(string url) =>
        Assert.Throws<ArgumentException>(() => PhoenixReview.Loopback(url));

    [Fact]
    public void Span_shows_history_question_answer_and_evidence()
    {
        var attributes = PhoenixReview
            .Attributes(Item("c1#0"), "gemini-1")
            .ToDictionary(a => a.Key, a => a.Value);
        Assert.Equal("CHAIN", attributes["openinference.span.kind"]);
        var input = (string)attributes["input.value"]!;
        Assert.Contains("Klient: Cześć\nAsystent: Dzień dobry", input.Replace("\r", ""));
        Assert.EndsWith("Klient: Aktywuj kupon rabat", input);
        Assert.Equal("Proponuję aktywację kuponu rabat.", attributes["output.value"]);
        using var metadata = JsonDocument.Parse((string)attributes["metadata"]!);
        Assert.Equal("SIM rabat", metadata.RootElement.GetProperty("proposal_target").GetString());
        Assert.Equal("c1#0", metadata.RootElement.GetProperty("item_id").GetString());
    }

    [Fact]
    public void Grades_are_human_pass_or_fail_the_latest_per_span()
    {
        using var body = JsonDocument.Parse(
            """
            {"data": [
              {"span_id": "aaaa", "name": "human_verdict", "annotator_kind": "HUMAN",
               "result": {"label": "pass"}, "updated_at": "2026-11-24T10:00:00+00:00"},
              {"span_id": "aaaa", "name": "human_verdict", "annotator_kind": "HUMAN",
               "result": {"label": "Fail", "explanation": "bez kroku"}, "updated_at": "2026-11-24T11:00:00+00:00"},
              {"span_id": "bbbb", "name": "human_verdict", "annotator_kind": "LLM",
               "result": {"label": "pass"}},
              {"span_id": "bbbb", "name": "human_verdict", "annotator_kind": "HUMAN",
               "result": {"label": "maybe"}},
              {"span_id": "bbbb", "name": "judge_verdict", "annotator_kind": "HUMAN",
               "result": {"label": "pass"}}
            ], "next_cursor": null}
            """
        );
        var grade = Assert.Single(PhoenixReview.ParseHuman(body.RootElement));
        Assert.Equal(new PhoenixReview.HumanGrade("aaaa", "fail", "bez kroku"), grade);
    }

    [Fact]
    public void Fill_writes_grades_by_item_and_keeps_the_rest()
    {
        var sheet = string.Join(
            '\n',
            "id\tscenario\tquestion\tanswer\ttools\tproposal_target\thuman_verdict\thuman_note",
            "c1#0\ts\tq\ta\tt\t\t\t",
            "c2#0\ts\tq\ta\tt\t\t\t",
            ""
        );
        var filled = PhoenixReview.Fill(sheet, File, [new("aaaa", "fail", "zła\tnota\nx")]);
        var lines = filled.Split('\n');
        Assert.Equal("c1#0\ts\tq\ta\tt\t\tfail\tzła nota x", lines[1]);
        Assert.Equal("c2#0\ts\tq\ta\tt\t\t\t", lines[2]);
        Assert.Equal(sheet.Split('\n')[0], lines[0]);
    }

    [Fact]
    public void Fill_clears_a_grade_removed_in_phoenix_but_not_other_rows()
    {
        var sheet = string.Join(
            '\n',
            "id\tscenario\tquestion\tanswer\ttools\tproposal_target\thuman_verdict\thuman_note",
            "c1#0\ts\tq\ta\tt\t\tfail\tstara",
            "c9#0\ts\tq\ta\tt\t\tpass\tspoza eksportu"
        );
        var lines = PhoenixReview.Fill(sheet, File, []).Split('\n');
        Assert.Equal("c1#0\ts\tq\ta\tt\t\t\t", lines[1]);
        Assert.Equal("c9#0\ts\tq\ta\tt\t\tpass\tspoza eksportu", lines[2]);
    }
}
