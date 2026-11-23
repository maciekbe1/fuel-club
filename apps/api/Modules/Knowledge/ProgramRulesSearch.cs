using System.Text.RegularExpressions;
using FuelClub.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Knowledge;

public sealed record RuleMatch(
    string Section,
    string Heading,
    string Text,
    double Coverage,
    double Similarity
);

/// <summary>
/// Finds program rule points for a Polish question in PostgreSQL, without a vector store.
/// PostgreSQL has no Polish dictionary, so inflection is handled on the query side: every word is cut
/// to a prefix (punktów/punkty → "punkt:*", nagrody/nagrodę → "nagro:*") and a few program terms
/// have synonyms ("maksymalny" → "najwyżej"). A fragment's coverage is the share of the question's
/// words it contains, each word weighted by how rare it is in the document (a word missing from the
/// whole document weighs the most, so a question about something else stays below the threshold).
/// Trigram similarity only orders fragments with close coverage. Calibrated on the cases in
/// ProgramRulesSearchTests (threshold, weights): change them together.
/// </summary>
public sealed partial class ProgramRulesSearch(AppDbContext db)
{
    public const int MaxResults = 3;
    public const double MinCoverage = 0.45;
    private const double SimilarityWeight = 0.3;

    private static readonly HashSet<string> StopWords =
    [
        .. (
            "a albo ale ani aby by byc co czy czym dla do gdy i ich ile jak jaki jaka jakie jest "
            + "jesli juz ma mam mi moge moj moja moje mojego mojej moich mnie na nad nie o od po pod "
            + "przez przy sa sie ta tak to tu w we z za ze zeby kiedy czemu dlaczego mozna moze"
        ).Split(' '),
    ];

    /// <summary>
    /// Program terms the rules phrase differently: a question word (keyed by its Stem, so the key
    /// always matches) and the prefixes the rules use instead.
    /// </summary>
    private static readonly Dictionary<string, string[]> Synonyms = new (
        string Word,
        string[] Rules
    )[]
    {
        ("maksymalny", ["najwy", "limit"]),
        ("hot", ["nagro", "produ"]),
        ("doga", ["nagro", "produ"]),
        ("kawa", ["nagro"]),
        ("myjnia", ["nagro"]),
        ("dodac", ["przyz"]),
        ("dopisac", ["przyz"]),
        ("dostane", ["przyz", "daje"]),
        ("waznosc", ["termi", "wygas"]),
        ("wygasa", ["termi", "wazno"]),
        ("traci", ["wygas", "termi"]),
        ("zwrot", ["anulo"]),
        ("oddac", ["zwro", "anulo"]),
        ("cofnac", ["anulo"]),
        ("stara", ["poprz"]),
        ("naraz", ["jedne"]),
        ("dwoch", ["jedne"]),
    }.ToDictionary(s => Stem(s.Word), s => s.Rules);

    /// <summary>
    /// Word groups of the question, each an OR of prefixes ("doda:* | przyz:*"). Only [a-z0-9]
    /// reach the tsquery text.
    /// </summary>
    public static IReadOnlyList<string> Terms(string question)
    {
        var stems = Word()
            .Matches(PolishText.Normalize(question))
            .Select(m => m.Value)
            .Where(w => w.Length > 1 && !StopWords.Contains(w))
            .Select(Stem)
            .Distinct()
            .Order(StringComparer.Ordinal);
        return
        [
            .. stems.Select(stem =>
                string.Join(
                    " | ",
                    new[] { stem }
                        .Concat(Synonyms.GetValueOrDefault(stem, []))
                        .Select(s => $"{s}:*")
                )
            ),
        ];
    }

    /// <summary>A crude Polish stem: the first letters, which inflection rarely changes.</summary>
    public static string Stem(string word) =>
        word.All(char.IsAsciiDigit) || word.Length <= 3
            ? word
            : word[..Math.Min(5, word.Length - 1)];

    public async Task<IReadOnlyList<RuleMatch>> SearchAsync(
        string question,
        CancellationToken cancellationToken
    )
    {
        var terms = Terms(question).ToArray();
        if (terms.Length == 0)
            return [];
        var normalized = PolishText.Normalize(question);
        var version = ProgramRules.CurrentVersion;
        return await db
            .Database.SqlQuery<RuleMatch>(
                $"""
                WITH terms AS (
                    SELECT t.ord, to_tsquery('simple', t.term) AS query
                    FROM unnest({terms}::text[]) WITH ORDINALITY AS t(term, ord)
                ),
                weights AS (
                    SELECT terms.ord, terms.query,
                        ln((total.n + 1)::float8
                            / (count(f."Id") FILTER (WHERE f."SearchVector" @@ terms.query) + 0.5))
                            AS weight
                    FROM terms
                    CROSS JOIN (SELECT count(*) AS n FROM "RuleFragments"
                        WHERE "DocumentVersion" = {version}) AS total
                    LEFT JOIN "RuleFragments" f ON f."DocumentVersion" = {version}
                    GROUP BY terms.ord, terms.query, total.n
                ),
                scored AS (
                    SELECT f."Section", f."Heading", f."Text",
                        sum(w.weight) FILTER (WHERE f."SearchVector" @@ w.query) / sum(w.weight)
                            AS "Coverage",
                        word_similarity({normalized}, f."SearchText")::float8 AS "Similarity"
                    FROM "RuleFragments" f CROSS JOIN weights w
                    WHERE f."DocumentVersion" = {version}
                    GROUP BY f."Id", f."Section", f."Heading", f."Text", f."SearchText"
                )
                SELECT "Section", "Heading", "Text", "Coverage", "Similarity"
                FROM scored
                WHERE "Coverage" >= {MinCoverage}
                ORDER BY "Coverage" + {SimilarityWeight} * "Similarity" DESC, "Section"
                LIMIT {MaxResults}
                """
            )
            .ToListAsync(cancellationToken);
    }

    [GeneratedRegex("[a-z0-9]+")]
    private static partial Regex Word();
}
