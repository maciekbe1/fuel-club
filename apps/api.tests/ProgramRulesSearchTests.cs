using System.Security.Cryptography;
using System.Text;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Knowledge;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace FuelClub.Api.Tests;

/// <summary>
/// The program rules index (P14): the migration loads the approved document unchanged, and Polish
/// questions find the right points despite inflection, or find nothing when the rules do not cover
/// them.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class ProgramRulesSearchTests(ApiFactory factory)
{
    /// <summary>The approved regulamin-v1 (owner, 2026-11-23). A change needs a new version.</summary>
    private const string ApprovedV1Sha256 =
        "7da1c58562a736b47aafb8940060619190f6526657c3658ad44ab2a67be843b6";

    [Fact]
    public void The_approved_version_is_not_edited_in_place()
    {
        var text = ProgramRules.Read("regulamin-v1");
        Assert.Equal(
            ApprovedV1Sha256,
            Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(text)))
        );
    }

    [Fact]
    public void The_document_splits_into_numbered_points_with_their_section()
    {
        var fragments = ProgramRules.Parse(ProgramRules.Read(ProgramRules.CurrentVersion));

        Assert.Equal(32, fragments.Count);
        Assert.Equal(fragments.Count, fragments.Select(f => f.Section).Distinct().Count());
        var points = fragments.Single(f => f.Section == "5.4");
        Assert.Equal("Aktywacja i odbiór kuponu", points.Heading);
        // Indented list lines belong to their point.
        Assert.Contains("jednego kuponu", points.Text);
        Assert.Equal("Punkty nie wygasają.", fragments.Single(f => f.Section == "6.1").Text);
    }

    [Fact]
    public async Task The_migration_loaded_exactly_the_current_document()
    {
        var expected = ProgramRules
            .Parse(ProgramRules.Read(ProgramRules.CurrentVersion))
            .Select(f => (f.Section, f.Heading, f.Text, ProgramRules.SearchText(f)))
            .OrderBy(f => f.Section, StringComparer.Ordinal)
            .ToList();
        await using var scope = factory.Services.CreateAsyncScope();
        var stored = (
            await scope
                .ServiceProvider.GetRequiredService<AppDbContext>()
                .RuleFragments.Where(f => f.DocumentVersion == ProgramRules.CurrentVersion)
                .Select(f => new
                {
                    f.Section,
                    f.Heading,
                    f.Text,
                    f.SearchText,
                })
                .ToListAsync()
        ).Select(f => (f.Section, f.Heading, f.Text, f.SearchText)).OrderBy(f => f.Section, StringComparer.Ordinal).ToList();

        Assert.Equal(expected, stored);
    }

    [Theory]
    [InlineData("Ile punktów dostanę, jeśli zapłacę po rabacie?", "3.2")]
    [InlineData("czy punkty wygasają", "6.1")]
    [InlineData("Kiedy kupon traci ważność?", "6.2")]
    [InlineData("Jak odebrać nagrodę, np. kawę?", "3.6|5.5")]
    [InlineData("czym się różni aktywacja od odbioru kuponu", "5.1|5.2|5.3")]
    [InlineData("Czy mogę cofnąć aktywację kuponu?", "5.2")]
    [InlineData("Chcę zwrot punktów za kupon", "4.4")]
    [InlineData("Zapomniałem karty, czy dopiszecie mi punkty za paragon?", "3.4")]
    [InlineData("Jaki jest maksymalny zakup?", "3.5")]
    [InlineData("Czy dostanę punkty za hot doga?", "3.6")]
    [InlineData("Ile punktów za 0,99 zł?", "3.1")]
    [InlineData("Czy mogę użyć dwóch kuponów naraz?", "5.4")]
    [InlineData("Co się stanie ze starą kartą po wymianie?", "2.4")]
    // Questions written after the threshold was set (not used to tune it).
    [InlineData("Czy za kupon na kawę dostanę punkty?", "3.6")]
    [InlineData("Czy wyłączenie oferty unieważnia mój kupon?", "4.3")]
    [InlineData("Ile punktów dostanę za 123,99 zł?", "3.1")]
    [InlineData("Dlaczego nie dostałem punktów, gdy rabat pokrył cały zakup?", "3.3")]
    [InlineData("Czy mogę zamienić kupon rabatowy na nagrodę?", "5.6")]
    [InlineData("Czy aktywny kupon ma termin?", "5.2|6.2")]
    [InlineData("Czy kod karty służy do płacenia?", "2.3")]
    [InlineData("Czy konsultant widzi moje saldo?", "8.3")]
    [InlineData("Jak liczony jest rabat na litr?", "5.4")]
    [InlineData("Czy asystent może mi dopisać punkty?", "7.2")]
    // Regression (Gemini pilot 2026-11-23): the program card, not a payment card.
    [InlineData("karta lojalnościowa", "2.1|2.2|2.3")]
    [InlineData("Do czego służy karta lojalnościowa?", "2.3")]
    [InlineData("Co regulamin mówi o karcie lojalnościowej?", "2.1|2.2|2.3")]
    // Regression (Gemini, 2026-11-23): short model queries tie at full coverage; both points
    // behind "punkty za hot doga" (purchase §3.1, reward pickup §3.6) must be among the results.
    [InlineData("punkty za hot doga", "3.1")]
    [InlineData("punkty za hot doga", "3.6")]
    [InlineData("hot doga", "3.1")]
    [InlineData("hot doga", "3.6")]
    [InlineData("Czy dostanę punkty, jeśli kupię hot doga?", "3.1")]
    [InlineData("Czy dostanę punkty za odebranie hot doga z kuponu?", "3.6")]
    // Regression (Gemini run gemini-1, 2026-11-23): "collecting" points is "Naliczanie" and
    // "dostaje" in the rules, and "how does it work" carries no topic. 0/3 before the fix.
    [InlineData("Jak działa zbieranie punktów?", "3.1")]
    [InlineData("zbieranie punktów", "3.1")]
    [InlineData("Jak zbierać punkty?", "3.1")]
    [InlineData("Jak zdobyć punkty?", "3.1")]
    [InlineData("Jak mogę uzbierać punkty?", "3.1")]
    [InlineData("Jak wygląda naliczanie punktów?", "3.1")]
    [InlineData("Jak wyglądają zasady zbierania punktów?", "3.1")]
    [InlineData("Ile punktów mogę zarobić?", "3.1")]
    public async Task A_question_finds_its_point_among_the_results(string question, string sections)
    {
        var found = await SearchAsync(question);

        Assert.NotEmpty(found);
        Assert.True(found.Count <= ProgramRulesSearch.MaxResults);
        Assert.Contains(found, m => sections.Split('|').Contains(m.Section));
        Assert.All(found, m => Assert.True(m.Coverage >= ProgramRulesSearch.MinCoverage));
    }

    [Theory]
    // The collecting and "how does it work" rules apply only to points (review 2026-11-24).
    [InlineData("Jak zdobyć kupon?")]
    [InlineData("Jak zbierać kupony?")]
    [InlineData("Jak zdobyć nagrodę?")]
    [InlineData("Jak działa myjnia?")]
    [InlineData("Jak działa paliwo?")]
    [InlineData("karta kredytowa")]
    [InlineData("czy mogę płacić kartą kredytową")]
    [InlineData("Ile kosztuje benzyna?")]
    [InlineData("Jakie są godziny otwarcia stacji?")]
    [InlineData("Jak zmienić hasło?")]
    [InlineData("Czy mogę zapłacić kartą kredytową?")]
    [InlineData("Ile wynosi cena oleju napędowego?")]
    [InlineData("Jak usunąć konto?")]
    [InlineData("Czy jest aplikacja na zegarek?")]
    [InlineData("??? !!!")]
    [InlineData("czy to jest")]
    [InlineData("Co regulamin mówi o płatności kartą kredytową?")]
    public async Task A_question_the_rules_do_not_cover_finds_nothing(string question)
    {
        Assert.Empty(await SearchAsync(question));
    }

    [Theory]
    [InlineData("punktów", "punkt:*")]
    [InlineData("punkty", "punkt:*")]
    [InlineData("nagrody", "nagro:*")]
    [InlineData("nagrodę", "nagro:*")]
    [InlineData("ważność", "wazno:* | termi:* | wygas:*")]
    // Synonym keys go through Stem too: a 5-letter word is cut to 4 letters.
    [InlineData("oddać", "odda:* | zwro:* | anulo:*")]
    [InlineData("myjnia", "myjni:* | nagro:*")]
    [InlineData("traci", "trac:* | wygas:* | termi:*")]
    public void Inflected_forms_share_one_term(string word, string term)
    {
        Assert.Equal([term], ProgramRulesSearch.Terms(word));
    }

    [Fact]
    public void Only_letters_and_digits_reach_the_query()
    {
        var terms = ProgramRulesSearch.Terms("kupon'); DROP TABLE x; -- :* & !");

        Assert.All(terms, t => Assert.Matches(@"^[a-z0-9]+:\*( \| [a-z0-9]+:\*)*$", t));
    }

    private async Task<IReadOnlyList<RuleMatch>> SearchAsync(string question)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<ProgramRulesSearch>()
            .SearchAsync(question, CancellationToken.None);
    }
}
