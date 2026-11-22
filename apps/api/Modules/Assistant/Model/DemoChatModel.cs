using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
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

    public string Provider => "demo";
    public string Model => "demo-rules";
    public string PromptVersion => "demo-rules-v1";
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

        var done = context.ToolResults;
        var activation = Activation().IsMatch(text);

        if (activation)
            return Task.FromResult(NextActivationStep(context, text, intro));

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
        // One coupon, or the one whose offer the message names; otherwise ask which one.
        var named = list.FirstOrDefault(c =>
            text.Contains(Normalize(c.OfferName), StringComparison.Ordinal)
        );
        if (named is not null)
            return Propose(named.Id.ToString());
        if (list.Count == 1)
            return Propose(list[0].Id.ToString());
        return new ModelStep.Reply(
            intro
                + "Który kupon aktywować? Do aktywacji masz: "
                + string.Join("; ", list.Select(c => c.OfferName))
                + ". Napisz nazwę oferty."
        );
    }

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
                        + $"{o.DiscountPerLiter.ToString("0.00", Polish)} zł/l do {o.MaxLiters.ToString("0.###", Polish)} l"
                    )
                )
                + ".",
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

    /// <summary>Lower case without Polish diacritics, so "Saldo", "salda" and "SALDO" match alike.</summary>
    private static string Normalize(string text)
    {
        var decomposed = text.ToLowerInvariant()
            .Replace('ł', 'l')
            .Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
            if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        return builder.ToString();
    }

    [GeneratedRegex(
        @"nalicz|dopisz|dodaj\s+(mi\s+)?\d*\s*punkt|daj\s+(mi\s+)?\d*\s*punkt|odbierz|zrealizuj|wymien|kup\s+kupon|admin|zmien\s+ofert|usun"
    )]
    private static partial Regex Forbidden();

    // Imperative and noun forms only: "aktywne kupony" is a question, not a request.
    [GeneratedRegex(@"aktywuj|aktywowac|aktywacj")]
    private static partial Regex Activation();

    [GeneratedRegex(@"sald|punkt|ile\s+mam")]
    private static partial Regex Balance();

    [GeneratedRegex(@"ofert|nagrod|katalog|co\s+moge")]
    private static partial Regex Offers();

    [GeneratedRegex(@"kupon")]
    private static partial Regex Coupons();

    [GeneratedRegex(@"[0-9a-fA-F]{8}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{4}-[0-9a-fA-F]{12}")]
    private static partial Regex CouponId();
}
