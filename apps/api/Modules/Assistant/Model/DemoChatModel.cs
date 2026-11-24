using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Assistant.Tools;

namespace FuelClub.Api.Modules.Assistant.Model;

/// <summary>
/// The default, deterministic model: keyword rules, no provider key, no network. It is labelled as a
/// demo (meta.demo, and its first answer says so). It only ever asks for tools from the closed list;
/// what it may see and do is decided by the tools, not by these rules.
/// </summary>
public sealed partial class DemoChatModel : IChatModel
{
    private static readonly CultureInfo Polish = CultureInfo.GetCultureInfo("pl-PL");

    /// <summary>The question that asks which coupon to activate; the next message answers it.</summary>
    private const string WhichCoupon = "Który kupon aktywować?";

    public string Provider => "demo";
    public string Model => "demo-rules";
    public string PromptVersion => "demo-rules-v10";
    public bool Demo => true;

    public Task<ModelStep> NextAsync(ChatModelContext context, CancellationToken cancellationToken)
    {
        var text = Normalize(context.Message);
        var intro = context.History.Count == 0 ? "Asystent w trybie demo (reguły, bez AI). " : "";

        if (Forbidden().IsMatch(text))
            return Reply(
                intro
                    + "Nie mogę naliczać ani wymieniać punktów, realizować kuponów ani zmieniać ofert. "
                    + "Punkty nalicza kasa przy tankowaniu, a wymianę zrobisz w zakładce „Oferty”."
            );

        // "Czy konsultant widzi saldo?" asks how the program works; other mentions of a person
        // ask for one.
        var aboutPeople = PeopleQuestion().IsMatch(text);

        // Asking for a person: only a proposal, the button opens the case.
        if (Handoff().IsMatch(text) && !aboutPeople)
            return context.ToolResults.Any(r => r.Name == AssistantTools.ProposeHandoff)
                ? Reply(
                    intro
                        + "Mogę przekazać rozmowę konsultantowi. Użyj przycisku „Porozmawiaj z "
                        + "konsultantem” - przed wysłaniem zobaczysz, co zostanie zapisane."
                )
                : Call(AssistantTools.ProposeHandoff);

        // A question about awarded points; a request in the same message was refused above.
        // A complaint gets the same check, said with the problem acknowledged, the next step and
        // a consultant proposal (the button) in case the purchase is not the one they mean.
        if (ExplainPoints().IsMatch(text))
        {
            var explained = context.ToolResults.LastOrDefault(r =>
                r.Name == AssistantTools.ExplainPurchasePoints
            );
            if (explained is null)
                return Call(AssistantTools.ExplainPurchasePoints);
            if (!Complaint().IsMatch(text))
                return Reply(intro + Describe(explained));
            if (context.ToolResults.All(r => r.Name != AssistantTools.ProposeHandoff))
                return Call(AssistantTools.ProposeHandoff);
            const string Consultant =
                "użyj przycisku „Porozmawiaj z konsultantem” - przekażę sprawę konsultantowi.";
            // Only a found purchase is "checked"; none or a failed lookup goes to the consultant.
            return Reply(
                intro
                    + "Przykro mi, że punkty się nie zgadzają. "
                    + (
                        explained.Data is PurchasePointsData
                            ? "Sprawdziłem: "
                                + LowerFirst(Describe(explained))
                                + " Jeśli to nie ten zakup albo nie widzisz tych punktów, "
                                + "odśwież aplikację. Jeśli to nie pomoże, "
                                + Consultant
                        : explained.Status == ToolStatus.Ok
                            ? "Nie widzę jeszcze żadnego Twojego zakupu. Jeśli tankowałeś "
                                + "z kartą, odśwież aplikację, a jeśli to nie pomoże, "
                                + Consultant
                        : "Nie udało się teraz sprawdzić Twojego zakupu - " + LowerFirst(Consultant)
                    )
            );
        }

        // "Punkty za hot doga?" is a purchase (§3.1, points only for fuel) or a reward pickup
        // (§3.6). Both mean no points, so it answers both instead of asking (human review of
        // demo-1): one search per meaning, each cited with its source. Not a question about the
        // cost or the validity of points ("ile punktów za kawę", "czy wygasają"): those go on.
        if (
            PointsForProduct().IsMatch(text)
            && !PurchaseOrPickup().IsMatch(text)
            && !NotEarning().IsMatch(text)
        )
            return Task.FromResult(NextProductStep(context, intro, text));

        // How points are earned is §3 of the rules: searched by its subject. Since 2026-11-24 the
        // search also matches "zbieranie punktów", but not "za co" or "sklep" (demo-1 regressions).
        if (EarningQuestion().IsMatch(text) && !Product().IsMatch(text))
            return Task.FromResult(NextRulesStep(context, intro, EarningQuery));

        // A question about the program: answered from the approved rules (P14), not from these
        // patterns. Only rule wording counts ("wygasa", "zasady", "cofnąć"), so a question about the
        // user's own coupon or points still reaches its tool below. One intent per message: in
        // "Ile mam punktów? Czy punkty wygasają?" only the rules are answered.

        if (aboutPeople || RulesQuestion().IsMatch(text))
            return Task.FromResult(NextRulesStep(context, intro));

        var done = context.ToolResults;
        // An answer to "which coupon?" continues the activation even without the keyword.
        var activation = Activation().IsMatch(text) || AnswersWhichCoupon(context.History, text);

        if (activation)
            return Task.FromResult(NextActivationStep(context, text, intro));

        if (Compare().IsMatch(text) && !ChoosesReward(text))
            return Task.FromResult(NextComparisonStep(context, text, intro));

        foreach (var tool in Wanted(text))
            if (done.All(result => result.Name != tool))
                return Call(tool);

        if (done.Count == 0)
            return Reply(
                intro
                    + "Mogę sprawdzić Twoje saldo, pokazać dostępne oferty i Twoje kupony "
                    + "albo zaproponować aktywację kuponu. O co chcesz zapytać?"
            );

        return Reply(intro + string.Join(" ", done.Select(Describe)));
    }

    private ModelStep NextActivationStep(ChatModelContext context, string text, string intro)
    {
        var done = context.ToolResults;
        var proposal = done.LastOrDefault(r => r.Name == AssistantTools.ProposeCouponActivation);
        if (proposal is not null)
            return new ModelStep.Reply(intro + Describe(proposal));

        // A coupon id in the message is used as given; the tool checks that it is the user's own.
        var id = CouponId().Match(context.Message);
        if (id.Success)
            return Propose(id.Value);

        var coupons = done.LastOrDefault(r => r.Name == AssistantTools.ListMyCoupons);
        if (coupons is null)
            return new ModelStep.Tool(
                new ToolCall(AssistantTools.ListMyCoupons, Arguments(new { status = "Purchased" }))
            );
        if (coupons.Status != ToolStatus.Ok || coupons.Data is not List<CouponSummary> list)
            return new ModelStep.Reply(intro + "Nie udało się sprawdzić Twoich kuponów.");
        if (list.Count == 0)
            return new ModelStep.Reply(
                intro + "Nie masz kuponów do aktywacji. Kupon dostaniesz w zakładce „Oferty”."
            );
        // What the customer named once the activation words are gone ("aktywuj kupon X" -> "x").
        // Nothing named: a general request, so the only coupon may be proposed.
        var named = NamedWords(text);
        if (named.Count == 0)
            return list.Count == 1 ? Propose(list[0].Id.ToString()) : AskWhich(intro, list);
        // Named: only a coupon whose offer has every named word; never another one in its place.
        var matching = list.Where(c => Names(c.OfferName, named)).ToList();
        if (matching.Count == 0)
            return AskWhich(intro + "Nie masz takiego kuponu do aktywacji. ", list);
        // Several coupons of one offer are the same thing: any of them is what was named.
        if (matching.Select(c => c.OfferName).Distinct().Count() == 1)
            return Propose(matching[0].Id.ToString());
        return AskWhich(intro, matching);
    }

    private static ModelStep AskWhich(string intro, IEnumerable<CouponSummary> coupons) =>
        new ModelStep.Reply(
            intro
                + WhichCoupon
                + Listed
                + string.Join("; ", coupons.Select(c => c.OfferName).Distinct())
                + ListEnd
        );

    /// <summary>
    /// The words of the message that name a coupon: without the activation words and filler
    /// ("aktywuj mój kupon" names nothing; "aktywuj kupon weekend" names "weekend").
    /// </summary>
    private static List<string> NamedWords(string text) =>
        NonWord()
            .Split(text)
            .Select(word => word.Trim('-'))
            .Where(word => word.Length > 0 && !Activation().IsMatch(word) && !Filler.Contains(word))
            .ToList();

    /// <summary>
    /// Every named word is a whole word of the offer name, or the same word inflected ("doga" for
    /// "dog", "kawe" for "kawa"): never a part of another word ("kawa" is not "kawalek").
    /// </summary>
    private static bool Names(string offerName, List<string> named)
    {
        var words = NonWord().Split(Normalize(offerName)).Select(w => w.Trim('-')).ToList();
        return named.All(word => words.Any(w => SameWord(w, word)));
    }

    private static bool SameWord(string offer, string named)
    {
        if (offer == named)
            return true;
        // Polish endings ("kawalek", "kawalka"): a shared stem of 3+ letters, all but the last two
        // letters of the shorter word, lengths differing by at most two.
        if (
            offer.Length < 3
            || named.Length < 3
            || Math.Abs(offer.Length - named.Length) > 2
            || !offer.All(char.IsLetter)
            || !named.All(char.IsLetter)
        )
            return false;
        var shared = 0;
        while (shared < Math.Min(offer.Length, named.Length) && offer[shared] == named[shared])
            shared++;
        return shared >= 3 && shared >= Math.Min(offer.Length, named.Length) - 2;
    }

    private static readonly HashSet<string> Filler =
    [
        "kupon",
        "kupony",
        "kuponu",
        "kuponow",
        "kuponem",
        "moj",
        "moja",
        "moje",
        "mojego",
        "moich",
        "swoj",
        "swoje",
        "prosze",
        "hej",
        "czesc",
        "dzien",
        "dobry",
        "dzieki",
        "dziekuje",
        "pls",
        "moge",
        "mozna",
        "moglbys",
        "moglabys",
        "jak",
        "sie",
        "bym",
        "chcesz",
        "chce",
        "chcialbym",
        "chcialabym",
        "mozesz",
        "mi",
        "mnie",
        "ten",
        "ta",
        "to",
        "tego",
        "jedyny",
        "jedyna",
        "jedyne",
        "go",
        "jej",
        "na",
        "o",
        "z",
        "za",
        "do",
        "i",
        "a",
        "czy",
        "teraz",
        "juz",
        "please",
    ];

    /// <summary>
    /// The subject of §3.1 (points for fuel): the search query for questions about earning points,
    /// whose own wording ("za co", "sklep") matches nothing in the rules.
    /// </summary>
    private const string EarningQuery = "Za każdy pełny 1 zł zapłacony za paliwo punkt";

    private static ModelStep NextProductStep(ChatModelContext context, string intro, string text)
    {
        var product = Product().Match(text).Value;
        var searches = context
            .ToolResults.Where(r => r.Name == AssistantTools.SearchProgramRules)
            .ToList();
        string[] queries =
        [
            EarningQuery,
            $"Czy dostanę punkty za odebranie nagrody {product} z kuponu?",
        ];
        if (searches.Count < queries.Length)
            return new ModelStep.Tool(
                new ToolCall(
                    AssistantTools.SearchProgramRules,
                    Arguments(new { query = queries[searches.Count] })
                )
            );
        if (searches.Any(s => s.Status != ToolStatus.Ok))
            return new ModelStep.Reply(
                intro + "Nie udało się sprawdzić regulaminu - spróbuj ponownie za chwilę."
            );
        // No fragment for a meaning: the tool already proposed the consultant (handoff event).
        if (
            searches
            is not [
                { Data: ProgramRulesData { Fragments: [var purchase, ..] } rules },
                { Data: ProgramRulesData { Fragments: [var pickup, ..] } },
            ]
        )
            return new ModelStep.Reply(
                intro
                    + "Regulamin programu tego nie opisuje. Mogę przekazać pytanie konsultantowi - "
                    + "użyj przycisku „Porozmawiaj z konsultantem”."
            );
        return new ModelStep.Reply(
            $"{intro}Nie - ani za zakup w sklepie stacji, ani za odbiór nagrody z kuponu. "
                + $"Zakup: {purchase.Text} ({purchase.Source}).{ShopPurchaseNote($"kupie {product}", purchase.Source)} "
                + $"Odbiór nagrody: {pickup.Text} ({pickup.Source}). Wersja: {rules.Version}."
        );
    }

    private static string LowerFirst(string text) =>
        text.Length == 0 ? text : char.ToLowerInvariant(text[0]) + text[1..];

    /// <summary>
    /// §3.1 grants points for fuel and says nothing about shop products: for a product purchase the
    /// conclusion is spelled out, so the fuel rule is not passed off as a rule about the product.
    /// </summary>
    private static string ShopPurchaseNote(string question, string source)
    {
        var text = Normalize(question);
        return
            source.EndsWith("§3.1", StringComparison.Ordinal)
            && Product().IsMatch(text)
            && Purchase().IsMatch(text)
            ? " Ten punkt dotyczy paliwa. Zakupów w sklepie stacji regulamin nie wymienia, "
                + "więc punktów za nie nie przewiduje."
            : "";
    }

    /// <summary>
    /// Searches the rules with the question; cites the best point, or says the rules do not cover it
    /// and proposes a person.
    /// </summary>
    private static ModelStep NextRulesStep(
        ChatModelContext context,
        string intro,
        string? query = null
    )
    {
        var found = context.ToolResults.LastOrDefault(r =>
            r.Name == AssistantTools.SearchProgramRules
        );
        // Whether buying a product earns points is §3.1 (fuel only), searched by that subject;
        // shop words ("sklep") match nothing in the rules. Only a question about getting points:
        // "jak odebrać kawę, którą kupiłem" keeps its own words.
        var message = Normalize(context.Message);
        if (
            query is null
            && Product().IsMatch(message)
            && Purchase().IsMatch(message)
            && GetsPoints().IsMatch(message)
        )
            query = EarningQuery;
        if (found is null)
        {
            query ??= context.Message.Trim();
            if (query.Length > AssistantTools.MaxRulesQueryLength)
            {
                var cut = AssistantTools.MaxRulesQueryLength;
                // Never half of a surrogate pair (an emoji on the boundary).
                if (char.IsHighSurrogate(query[cut - 1]))
                    cut--;
                query = query[..cut];
            }
            return new ModelStep.Tool(
                new ToolCall(AssistantTools.SearchProgramRules, Arguments(new { query }))
            );
        }
        if (found.Data is ProgramRulesData { Fragments: [var best, ..] } rules)
            return new ModelStep.Reply(
                $"{intro}Według regulaminu: {best.Text} Źródło: {best.Source} ({rules.Version})."
                    + ShopPurchaseNote(context.Message, best.Source)
            );
        if (found.Status != ToolStatus.Ok)
            return new ModelStep.Reply(
                intro + "Nie udało się sprawdzić regulaminu - spróbuj ponownie za chwilę."
            );
        // No source: the tool itself proposed the consultant (handoff event).
        return new ModelStep.Reply(
            intro
                + "Regulamin programu tego nie opisuje. Mogę przekazać pytanie konsultantowi - "
                + "użyj przycisku „Porozmawiaj z konsultantem”."
        );
    }

    private const string Listed = " Do aktywacji masz: ";
    private const string ListEnd = ". Napisz nazwę oferty.";

    /// <summary>
    /// The message answers the previous "which coupon?" only when it names one of the offers that
    /// question listed; anything else ("ile mam punktów?") goes to the normal rules.
    /// </summary>
    private static bool AnswersWhichCoupon(IReadOnlyList<Chat.ChatHistoryItem> history, string text)
    {
        if (history.Count == 0 || history[^1] is not { Role: "assistant", Text: { } last })
            return false;
        var start = last.IndexOf(WhichCoupon + Listed, StringComparison.Ordinal);
        var end = last.LastIndexOf(ListEnd, StringComparison.Ordinal);
        if (start < 0 || end < start)
            return false;
        var from = start + WhichCoupon.Length + Listed.Length;
        var names = last[from..end].Split("; ").Select(Normalize);
        var answer = text.Trim().TrimEnd('.', '!', '?');
        return names.Any(name =>
            text.Contains(name, StringComparison.Ordinal)
            || (answer.Length >= 3 && name.Contains(answer, StringComparison.Ordinal))
        );
    }

    private static ModelStep NextComparisonStep(ChatModelContext context, string text, string intro)
    {
        var done = context.ToolResults.LastOrDefault(r => r.Name == AssistantTools.CompareCoupons);
        if (done is not null)
            return new ModelStep.Reply(intro + Describe(done));
        var liters = Number(LitersAmount().Match(text));
        if (liters is null)
            return new ModelStep.Reply(
                intro + "Ile litrów chcesz zatankować? Napisz np. „40 l za 250 zł”."
            );
        var gross = Number(MoneyAmount().Match(text));
        return new ModelStep.Tool(
            new ToolCall(
                AssistantTools.CompareCoupons,
                gross is null
                    ? Arguments(new { liters })
                    : Arguments(new { liters, grossAmount = gross })
            )
        );
    }

    private static decimal? Number(Match match) =>
        match.Success
        && decimal.TryParse(
            match.Groups[1].Value.Replace(" ", "").Replace(',', '.'),
            NumberStyles.AllowDecimalPoint,
            CultureInfo.InvariantCulture,
            out var value
        )
            ? value
            : null;

    private static string Money(decimal value) => $"{value.ToString("N2", Polish)} zł";

    private static ModelStep Propose(string couponId) =>
        new ModelStep.Tool(
            new ToolCall(AssistantTools.ProposeCouponActivation, Arguments(new { couponId }))
        );

    private static IEnumerable<string> Wanted(string text)
    {
        if (Balance().IsMatch(text))
            yield return AssistantTools.GetMyBalance;
        if (Offers().IsMatch(text))
            yield return AssistantTools.ListAvailableRewards;
        if (Coupons().IsMatch(text))
            yield return AssistantTools.ListMyCoupons;
    }

    private static string Describe(ToolResult result)
    {
        if (result.Status == ToolStatus.Error)
            return "Nie udało się pobrać danych - spróbuj ponownie za chwilę.";
        return (result.Name, result.Data) switch
        {
            (AssistantTools.GetMyBalance, BalanceData balance) =>
                $"Masz {balance.Balance.ToString("N0", Polish)} pkt.",
            (AssistantTools.ListAvailableRewards, List<RewardSummary> { Count: 0 }) =>
                "Nie ma teraz dostępnych ofert.",
            (AssistantTools.ListAvailableRewards, List<RewardSummary> offers) => "Dostępne oferty: "
                + string.Join(
                    "; ",
                    offers.Select(o =>
                        $"{o.Name} - {o.PointsCost.ToString("N0", Polish)} pkt, "
                        + (
                            o is { DiscountPerLiter: { } rate, MaxLiters: { } liters }
                                ? $"{rate.ToString("0.00", Polish)} zł/l do {liters.ToString("0.###", Polish)} l"
                                    + (
                                        o.MinLiters is { } min
                                            ? $" przy tankowaniu od {min.ToString("0.###", Polish)} l"
                                            : ""
                                    )
                                : "nagroda"
                        )
                        + (
                            o.PointsMissing > 0
                                ? $" (brakuje {o.PointsMissing.ToString("N0", Polish)} pkt)"
                                : " (stać Cię)"
                        )
                    )
                )
                + ".",
            (AssistantTools.CompareCoupons, CouponComparison c) => DescribeComparison(c),
            (AssistantTools.CompareCoupons, null) =>
                "Nie mogę policzyć tego tankowania. Podaj litry (więcej niż 0) i ewentualnie "
                    + "kwotę do 5 000 zł, np. „40 l za 250 zł”.",
            (AssistantTools.ExplainPurchasePoints, PurchasePointsData p) =>
                $"Ostatni zakup: brutto {Money(p.GrossAmount)}, rabat {Money(p.Discount)}, "
                    + $"zapłacono {Money(p.PaidAmount)} → {p.PointsAwarded.ToString("N0", Polish)} pkt. "
                    + p.Rule,
            (AssistantTools.ExplainPurchasePoints, null) =>
                "Nie widzę jeszcze żadnego Twojego zakupu, więc nie ma czego wyjaśnić.",
            (AssistantTools.ListMyCoupons, List<CouponSummary> { Count: 0 }) =>
                "Nie masz jeszcze kuponów.",
            (AssistantTools.ListMyCoupons, List<CouponSummary> coupons) => "Twoje kupony: "
                + string.Join("; ", coupons.Select(c => $"{c.OfferName} ({StatusLabel(c.Status)})"))
                + ". Kod aktywnego kuponu jest w zakładce „Moje kupony”.",
            (AssistantTools.ProposeCouponActivation, ProposalData proposal) =>
                $"Proponuję aktywację kuponu „{proposal.OfferName}”. "
                    + "Potwierdź przyciskiem poniżej - bez potwierdzenia nic się nie zmieni.",
            (AssistantTools.ProposeCouponActivation, _) => result.Reason switch
            {
                "coupon_already_active" => "Ten kupon jest już aktywny.",
                "coupon_redeemed" => "Ten kupon został już wykorzystany.",
                _ => "Nie ma takiego kuponu na Twoim koncie.",
            },
            _ => "Nie mogę tego zrobić.",
        };
    }

    private static string DescribeComparison(CouponComparison c)
    {
        var head =
            $"Dla {c.Liters.ToString("0.###", Polish)} l"
            + (c.GrossAmount is { } g ? $" za {Money(g)}" : "")
            + ". Kolejność: najwyższy rabat w zł.";
        if (c.Estimate)
            head += " To szacunek - rabat nie przekroczy kwoty zakupu, której nie podano.";
        var own =
            c.OwnCoupons.Count == 0
                ? "Nie masz kuponów do użycia."
                : "Twoje kupony: "
                    + string.Join(
                        "; ",
                        c.OwnCoupons.Select(o =>
                            $"{o.OfferName} ({StatusLabel(o.Status)}) - rabat {Money(o.Discount)}"
                            + BelowMinimum(c.Liters, o.MinLiters)
                        )
                    )
                    + ".";
        var offers =
            c.OffersForPoints.Count == 0
                ? "Nie ma ofert za punkty."
                : "Oferty za punkty: "
                    + string.Join(
                        "; ",
                        c.OffersForPoints.Select(o =>
                            $"{o.Name} - {o.PointsCost.ToString("N0", Polish)} pkt, rabat {Money(o.Discount)}"
                            + BelowMinimum(c.Liters, o.MinLiters)
                            + (
                                o.PointsMissing > 0
                                    ? $", brakuje {o.PointsMissing.ToString("N0", Polish)} pkt"
                                    : ""
                            )
                        )
                    )
                    + ".";
        return $"{head} {own} {offers}";
    }

    private static string BelowMinimum(decimal liters, decimal? minLiters) =>
        minLiters is { } min && liters < min
            ? $" (działa od {min.ToString("0.###", Polish)} l)"
            : "";

    private static string StatusLabel(string status) =>
        status switch
        {
            "Purchased" => "do aktywacji",
            "Active" => "aktywny",
            "Redeemed" => "wykorzystany",
            _ => status,
        };

    private static Task<ModelStep> Reply(string text) =>
        Task.FromResult<ModelStep>(new ModelStep.Reply(text));

    private static Task<ModelStep> Call(string tool) =>
        Task.FromResult<ModelStep>(new ModelStep.Tool(new ToolCall(tool, Arguments(new { }))));

    private static JsonElement Arguments(object value) => JsonSerializer.SerializeToElement(value);

    private static string Normalize(string text) => PolishText.Normalize(text);

    [GeneratedRegex(
        @"nalicz(\s|$|cie)|dopisz|dodaj\s+(mi\s+)?\d*\s*punkt|daj\s+(mi\s+)?\d*\s*punkt|odbierz|zrealizuj|wymien|kup\s+kupon|admin|zmien\s+ofert|usun"
    )]
    private static partial Regex Forbidden();

    // Wording of questions about how the program works, not about the user's own data. "Ile punktów
    // za litr / za 100 zł" asks for the accrual rule, not for the balance.
    [GeneratedRegex(
        @"regulamin|zasad|wygasa|termin|waznos|zwrot|anulow|cofn|limit|maksymal|najwiecej|jednorazow|dwoch\s+kupon|licza?\s+sie|liczon|czy\s+(dostane|dostaje)\s+punkt|jak\s+(dziala|odebrac)|do\s+czego\s+sluz\w*\s+(karta|kod|program|punkt)|punkt\w*\s+(\w+\s+)?za\s+(litr|zlot|\d)"
    )]
    private static partial Regex RulesQuestion();

    // "Czy dostanę punkty za hot doga?", "punkty za kawę" - not "ile punktów kosztuje kawa".
    [GeneratedRegex(@"punkt\w*\s+za\s+(hot\s*dog|kaw|myjni)")]
    private static partial Regex PointsForProduct();

    [GeneratedRegex(@"hot\s*dog\w*|kaw\w*|myjni\w*")]
    private static partial Regex Product();

    [GeneratedRegex(@"zakup|kupi|kupuj|sklep|place|zaplac")]
    private static partial Regex Purchase();

    [GeneratedRegex(@"kup|zaplac|place|odbi|odbier|odebr|kupon|wymian|wymien|nagrod|sklep")]
    private static partial Regex PurchaseOrPickup();

    [GeneratedRegex(@"czy\s+(konsultant|asystent)\w*\s+(moze|widzi|ma|zmien)")]
    private static partial Regex PeopleQuestion();

    [GeneratedRegex(@"konsultant|czlowiek|pracownik|z\s+osoba|obsluga\s+klienta")]
    private static partial Regex Handoff();

    // Imperative and noun forms only: "aktywne kupony" is a question, not a request.
    [GeneratedRegex(@"aktywuj|aktywowac|aktywacj")]
    private static partial Regex Activation();

    [GeneratedRegex(@"sald|punkt|ile\s+mam")]
    private static partial Regex Balance();

    // How points are earned, not how many there are: "jak działa zbieranie punktów", "za co
    // dostaję punkty", "jak zdobyć punkty".
    [GeneratedRegex(
        @"zbiera\w*\s+punkt|zbieranie|za\s+co\s+(\w+\s+)?(dostaj|dostan|przyznaj|nalicz|sa)\w*\s+punkt|jak\s+(zdoby|dosta|zbiera|uzbiera)\w*\s+punkt"
    )]
    private static partial Regex EarningQuestion();

    // A complaint about missing points (acknowledged and given a way out, not only explained).
    [GeneratedRegex(@"skandal|oszuk|zlodziej|wkurz|reklamac|nie\s+dostal")]
    private static partial Regex Complaint();

    // Getting points ("czy dostanę punkty", "punkty za/jeśli"), not anything else about a product.
    [GeneratedRegex(
        @"(dostane|dostaje|dostac|zdobe|zdoby|zbiore|nalicz)\w*\s+(\w+\s+)?punkt|punkt\w*\s+(za|jesli|gdy|kiedy)"
    )]
    private static partial Regex GetsPoints();

    // The cost or the validity of points, not whether a product earns them.
    [GeneratedRegex(@"ile\s+punkt|kosztuj|potrzebuj|brakuj|wygasa|waznos|termin")]
    private static partial Regex NotEarning();

    [GeneratedRegex(@"ofert|nagrod|katalog|co\s+moge")]
    private static partial Regex Offers();

    [GeneratedRegex(@"kupon")]
    private static partial Regex Coupons();

    [GeneratedRegex(
        @"(dostal|naliczon|naliczyl|przyznan)[^?]*(punkt|pkt)|(punkt|pkt)[^?]*(dostal|naliczon|naliczyl|przyznan|za\s+(ostatnie\s+)?tankowanie)"
    )]
    private static partial Regex ExplainPoints();

    [GeneratedRegex(@"(ktory|jaki)\s+kupon|oplaca|wybrac|zaoszczedz|porownaj|najwiekszy\s+rabat")]
    private static partial Regex Compare();

    // "Jakie nagrody mogę wybrać?" asks for the catalogue, not for a coupon comparison (demo-1).
    // Only "wybrać" next to rewards; "która oferta się opłaca" stays a comparison.
    private static bool ChoosesReward(string text) =>
        RewardChoice().IsMatch(text)
        && !StrongCompare().IsMatch(text)
        && !Coupons().IsMatch(text)
        && !LitersAmount().IsMatch(text);

    [GeneratedRegex(@"nagrod\w*\s+(\w+\s+){0,3}wybrac|wybrac\s+(\w+\s+){0,2}nagrod")]
    private static partial Regex RewardChoice();

    [GeneratedRegex(@"(ktory|jaki)\s+kupon|oplaca|zaoszczedz|porownaj|najwiekszy\s+rabat")]
    private static partial Regex StrongCompare();

    [GeneratedRegex(@"(\d+(?:[.,]\d+)?)\s*(?:l\b|litr)")]
    private static partial Regex LitersAmount();

    // The purchase amount, not a price per litre ("po 6,20 zł/l"); "1 000 zł" is one number.
    [GeneratedRegex(
        @"(?<![\d.,])(?<!po\s)(\d{1,3}(?:\s\d{3})+(?:[.,]\d+)?|\d+(?:[.,]\d+)?)\s*(?:zl|pln)(?!\s*/\s*l)(?!\s+za\s+litr)"
    )]
    private static partial Regex MoneyAmount();

    // Word separators; a hyphen stays inside a name ("demo-1").
    [GeneratedRegex(@"[^\p{L}\p{N}-]+")]
    private static partial Regex NonWord();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex CouponId();
}
