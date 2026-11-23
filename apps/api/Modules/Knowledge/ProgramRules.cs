using System.Text.RegularExpressions;
using FuelClub.Api.Infrastructure;

namespace FuelClub.Api.Modules.Knowledge;

/// <summary>One numbered point of the program rules, e.g. §3.2 under "Naliczanie punktów".</summary>
public sealed record RuleFragmentSource(string Section, string Heading, string Text);

/// <summary>
/// The approved program rules (P14). Each version is an embedded markdown file that never changes
/// once approved: a new version is a new file and a new migration that loads it.
/// </summary>
public static partial class ProgramRules
{
    public const string CurrentVersion = "regulamin-v1";
    public const string DocumentName = "Regulamin";

    public static string Read(string version)
    {
        using var stream =
            typeof(ProgramRules).Assembly.GetManifestResourceStream($"Knowledge.{version}.md")
            ?? throw new InvalidOperationException($"Program rules '{version}' are not embedded.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    /// <summary>
    /// Splits the document into numbered points: "## §N. Heading" opens a section, "1. text" a
    /// point, and indented lines continue the point. Anything else (title, status) is not a fragment.
    /// </summary>
    public static IReadOnlyList<RuleFragmentSource> Parse(string markdown)
    {
        var fragments = new List<RuleFragmentSource>();
        string? section = null;
        string? heading = null;
        foreach (var line in markdown.Split('\n').Select(l => l.TrimEnd('\r')))
        {
            if (SectionLine().Match(line) is { Success: true } s)
            {
                (section, heading) = (s.Groups[1].Value, s.Groups[2].Value.Trim());
                continue;
            }
            if (section is null)
                continue;
            if (PointLine().Match(line) is { Success: true } p)
                fragments.Add(
                    new($"{section}.{p.Groups[1].Value}", heading!, p.Groups[2].Value.Trim())
                );
            else if (line.StartsWith("   ") && line.Trim().Length > 0 && fragments.Count > 0)
                fragments[^1] = fragments[^1] with { Text = $"{fragments[^1].Text} {line.Trim()}" };
        }
        return fragments;
    }

    /// <summary>What the index stores and trigrams compare: heading and text, normalized.</summary>
    public static string SearchText(RuleFragmentSource fragment) =>
        PolishText.Normalize($"{fragment.Heading} {fragment.Text}");

    [GeneratedRegex(@"^## §(\d+)\. (.+)$")]
    private static partial Regex SectionLine();

    [GeneratedRegex(@"^(\d+)\. (.+)$")]
    private static partial Regex PointLine();
}
