using System.Text.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Assistant.Chat;
using FuelClub.Api.Modules.Assistant.Confirmation;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Loyalty.Points;
using FuelClub.Api.Modules.Loyalty.Wallet;
using FuelClub.Api.Modules.Rewards.Catalog;
using FuelClub.Api.Modules.Rewards.Coupons;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Assistant.Tools;

/// <summary>A request from the model. Arguments are untrusted JSON.</summary>
public sealed record ToolCall(string Name, JsonElement Arguments)
{
    /// <summary>The provider's own form of this call (e.g. a Gemini part with its thought signature).</summary>
    public JsonElement? ProviderPart { get; init; }

    /// <summary>Which model step asked for it (0, 1, ...); calls of one step go back together.</summary>
    public int Step { get; init; }
}

/// <summary>Who is asking: always the session user, never a model argument.</summary>
public sealed record ToolContext(string UserId);

public static class ToolStatus
{
    public const string Ok = "ok";
    public const string Error = "error";
    public const string Rejected = "rejected";
}

/// <summary>
/// The outcome for the model. <c>Reason</c> explains a rejection; <c>Confirmation</c> is set only by
/// propose_coupon_activation and <c>Handoff</c> only by propose_handoff - both go to the client as
/// events, not to the model.
/// </summary>
public sealed record ToolResult(
    string Name,
    string Status,
    object? Data = null,
    string? Reason = null,
    ChatConfirmationEvent? Confirmation = null,
    ChatHandoffEvent? Handoff = null
);

/// <summary>What the model sees of propose_handoff: only that the button is shown, nothing was handed over.</summary>
public sealed record HandoffProposalData(bool ButtonShown);

public sealed record BalanceData(int Balance);

/// <summary>PointsMissing = max(0, cost - balance), computed here, never by the model.</summary>
public sealed record RewardSummary(
    string Name,
    int PointsCost,
    string Kind,
    decimal? DiscountPerLiter,
    decimal? MaxLiters,
    int PointsMissing
);

/// <summary>A coupon the user already has (Purchased or Active), with its discount for the fill-up.</summary>
public sealed record CouponOption(
    Guid CouponId,
    string OfferName,
    string Status,
    decimal Discount,
    decimal LitersCovered
);

/// <summary>An offer that costs points first; Discount is what its coupon would give.</summary>
public sealed record OfferOption(
    string Name,
    int PointsCost,
    int PointsMissing,
    decimal Discount,
    decimal LitersCovered
);

/// <summary>
/// Both lists sorted by Criterion. Estimate: no gross amount was given, so the discount may still be
/// capped by the purchase amount.
/// </summary>
public sealed record CouponComparison(
    decimal Liters,
    decimal? GrossAmount,
    bool Estimate,
    string Criterion,
    List<CouponOption> OwnCoupons,
    List<OfferOption> OffersForPoints
);

/// <summary>The approved export for Gemini: amounts, points and the rule (ADR 0011).</summary>
public sealed record PurchasePointsData(
    decimal GrossAmount,
    decimal Discount,
    decimal PaidAmount,
    int PointsAwarded,
    string Rule
);

/// <summary>Kind: FuelDiscount (with per-liter terms) or Product (a reward handed over, no terms).</summary>
public sealed record CouponSummary(
    Guid Id,
    string OfferName,
    string Kind,
    string Status,
    decimal? DiscountPerLiter,
    decimal? MaxLiters
);

public sealed record ProposalData(Guid ProposalId, Guid CouponId, string OfferName);

/// <summary>
/// The closed list of assistant tools. Every tool reads the session user's data only; none takes a
/// user argument, none mutates (the two propose_* tools only show a button). Unknown tools and bad arguments are rejected without running.
/// </summary>
public sealed class AssistantTools(AppDbContext db, ConfirmationTokens tokens)
{
    public const string GetMyBalance = "get_my_balance";
    public const string ListAvailableRewards = "list_available_rewards";
    public const string ListMyCoupons = "list_my_coupons";
    public const string ProposeCouponActivation = "propose_coupon_activation";
    public const string CompareCoupons = "compare_coupons";
    public const string ExplainPurchasePoints = "explain_purchase_points";
    public const string ProposeHandoff = "propose_handoff";

    public const string PointsRule =
        "Obecna zasada naliczania: 1 pkt za każdą pełną złotówkę zapłaconą po rabacie "
        + "(zapłacono = brutto - rabat), zaokrąglenie w dół.";

    public const string ComparisonCriterion =
        "najwyższy rabat w zł dla podanego tankowania; przy równym rabacie - kolejność nazw";

    public static readonly IReadOnlyList<string> Names =
    [
        GetMyBalance,
        ListAvailableRewards,
        ListMyCoupons,
        ProposeCouponActivation,
        CompareCoupons,
        ExplainPurchasePoints,
        ProposeHandoff,
    ];

    public Task<ToolResult> ExecuteAsync(
        ToolCall call,
        ToolContext context,
        CancellationToken cancellationToken
    ) =>
        call.Name switch
        {
            GetMyBalance => NoArguments(call) ?? BalanceAsync(context, cancellationToken),
            ListAvailableRewards => NoArguments(call) ?? RewardsAsync(context, cancellationToken),
            CompareCoupons => CompareAsync(call, context, cancellationToken),
            ExplainPurchasePoints => NoArguments(call) ?? ExplainAsync(context, cancellationToken),
            ListMyCoupons => CouponsAsync(call, context, cancellationToken),
            ProposeCouponActivation => ProposeAsync(call, context, cancellationToken),
            // Changes nothing (ADR 0012): the client gets a "talk to an agent" button; only its
            // press, with the consent shown, opens a case.
            ProposeHandoff => NoArguments(call)
                ?? Task.FromResult(
                    new ToolResult(
                        ProposeHandoff,
                        ToolStatus.Ok,
                        new HandoffProposalData(true),
                        Handoff: new ChatHandoffEvent()
                    )
                ),
            // The name is model output: it is not echoed back.
            _ => Task.FromResult(Reject("unknown", "unknown_tool")),
        };

    private static ToolResult Reject(string name, string reason) =>
        new(name, ToolStatus.Rejected, Reason: reason);

    /// <summary>Null when the arguments are empty ({} or absent); a rejection otherwise.</summary>
    private static Task<ToolResult>? NoArguments(ToolCall call) =>
        IsEmpty(call.Arguments) ? null : Task.FromResult(Reject(call.Name, "invalid_arguments"));

    private static bool IsEmpty(JsonElement arguments) =>
        arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
        || (arguments.ValueKind == JsonValueKind.Object && !arguments.EnumerateObject().Any());

    /// <summary>Only the named properties, each a string; anything else rejects the call.</summary>
    private static Dictionary<string, string>? StringArguments(
        JsonElement arguments,
        params string[] allowed
    )
    {
        var values = new Dictionary<string, string>(StringComparer.Ordinal);
        if (arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return values;
        if (arguments.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in arguments.EnumerateObject())
        {
            if (
                !allowed.Contains(property.Name)
                || property.Value.ValueKind != JsonValueKind.String
            )
                return null;
            values[property.Name] = property.Value.GetString()!;
        }
        return values;
    }

    private async Task<ToolResult> BalanceAsync(ToolContext context, CancellationToken ct) =>
        new(
            GetMyBalance,
            ToolStatus.Ok,
            new BalanceData(await WalletEndpoints.BalanceAsync(db, context.UserId, ct))
        );

    private async Task<ToolResult> RewardsAsync(ToolContext context, CancellationToken ct)
    {
        var balance = await WalletEndpoints.BalanceAsync(db, context.UserId, ct);
        var offers = (await CatalogEndpoint.ActiveOffers(db).ToListAsync(ct))
            .Select(offer => new RewardSummary(
                offer.Name,
                offer.PointsCost,
                offer.Kind,
                offer.DiscountPerLiter,
                offer.MaxLiters,
                Missing(offer.PointsCost, balance)
            ))
            .ToList();
        return new(ListAvailableRewards, ToolStatus.Ok, offers);
    }

    private static int Missing(int cost, int balance) => Math.Max(0, cost - balance);

    /// <summary>
    /// Discounts for a planned fill-up, computed with the same formula as the till. Own usable
    /// coupons and offers that cost points are kept apart; nothing is called "best" here.
    /// </summary>
    private async Task<ToolResult> CompareAsync(
        ToolCall call,
        ToolContext context,
        CancellationToken ct
    )
    {
        var arguments = NumberArguments(call.Arguments, "liters", "grossAmount");
        if (
            arguments is null
            || !arguments.TryGetValue("liters", out var liters)
            || liters <= 0m
            || liters > FuelPurchaseRules.MaxLiters
            || decimal.Round(liters, FuelPurchaseRules.LitersScale) != liters
        )
            return Reject(call.Name, "invalid_arguments");
        decimal? gross = arguments.TryGetValue("grossAmount", out var g) ? g : null;
        if (
            gross is { } amount
            && (
                amount <= 0m
                || amount > FuelPurchaseRules.MaxAmount
                || decimal.Round(amount, FuelPurchaseRules.AmountScale) != amount
            )
        )
            return Reject(call.Name, "invalid_arguments");

        (decimal Discount, decimal Covered) Benefit(decimal perLiter, decimal maxLiters)
        {
            var covered = Math.Min(liters, maxLiters);
            var discount = gross is { } total
                ? CouponDiscount.Calculate(perLiter, maxLiters, liters, total).Discount
                : Math.Round(perLiter * covered, 2, MidpointRounding.AwayFromZero);
            return (discount, covered);
        }

        var balance = await WalletEndpoints.BalanceAsync(db, context.UserId, ct);
        var own = (
            await MyCouponsEndpoints
                .Own(db, context.UserId)
                // Fuel discounts only: a product has no amount in zł to compare.
                .Where(c => c.Status == CouponStatus.Purchased || c.Status == CouponStatus.Active)
                .Where(c => c.Kind == RewardKind.FuelDiscount)
                .Select(c => new
                {
                    c.Id,
                    c.OfferName,
                    c.Status,
                    DiscountPerLiter = c.DiscountPerLiter!.Value,
                    MaxLiters = c.MaxLiters!.Value,
                })
                .ToListAsync(ct)
        )
            .Select(c =>
            {
                var (discount, covered) = Benefit(c.DiscountPerLiter, c.MaxLiters);
                return new CouponOption(c.Id, c.OfferName, c.Status.ToString(), discount, covered);
            })
            .OrderByDescending(c => c.Discount)
            .ThenBy(c => c.OfferName, StringComparer.Ordinal)
            .ThenBy(c => c.CouponId)
            .ToList();
        var offers = (await CatalogEndpoint.ActiveOffers(db).ToListAsync(ct))
            .Where(o => o.Kind == nameof(RewardKind.FuelDiscount))
            .Select(o =>
            {
                var (discount, covered) = Benefit(o.DiscountPerLiter!.Value, o.MaxLiters!.Value);
                return new OfferOption(
                    o.Name,
                    o.PointsCost,
                    Missing(o.PointsCost, balance),
                    discount,
                    covered
                );
            })
            .OrderByDescending(o => o.Discount)
            .ThenBy(o => o.Name, StringComparer.Ordinal)
            .ToList();
        return new(
            CompareCoupons,
            ToolStatus.Ok,
            new CouponComparison(liters, gross, gross is null, ComparisonCriterion, own, offers)
        );
    }

    /// <summary>The latest own purchase; none is an answer too (no data, reason no_purchases).</summary>
    private async Task<ToolResult> ExplainAsync(ToolContext context, CancellationToken ct)
    {
        var purchase = await PurchasePoints.LatestAsync(db, context.UserId, ct);
        if (purchase is null)
            return new(ExplainPurchasePoints, ToolStatus.Ok, Reason: "no_purchases");
        return new(
            ExplainPurchasePoints,
            ToolStatus.Ok,
            new PurchasePointsData(
                purchase.GrossAmount,
                purchase.Discount,
                purchase.PaidAmount,
                purchase.PointsAwarded,
                PointsRule
            )
        );
    }

    /// <summary>Only the named properties, each a JSON number; anything else rejects the call.</summary>
    private static Dictionary<string, decimal>? NumberArguments(
        JsonElement arguments,
        params string[] allowed
    )
    {
        var values = new Dictionary<string, decimal>(StringComparer.Ordinal);
        if (arguments.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            return values;
        if (arguments.ValueKind != JsonValueKind.Object)
            return null;
        foreach (var property in arguments.EnumerateObject())
        {
            // An explicit null is an absent optional value (providers send it).
            if (allowed.Contains(property.Name) && property.Value.ValueKind == JsonValueKind.Null)
                continue;
            if (
                !allowed.Contains(property.Name)
                || property.Value.ValueKind != JsonValueKind.Number
                || !property.Value.TryGetDecimal(out var value)
            )
                return null;
            values[property.Name] = value;
        }
        return values;
    }

    private async Task<ToolResult> CouponsAsync(
        ToolCall call,
        ToolContext context,
        CancellationToken ct
    )
    {
        var arguments = StringArguments(call.Arguments, "status");
        if (arguments is null)
            return Reject(call.Name, "invalid_arguments");
        CouponStatus? status = null;
        if (arguments.TryGetValue("status", out var text))
        {
            if (
                !text.All(char.IsAsciiLetter)
                || !Enum.TryParse<CouponStatus>(text, ignoreCase: false, out var parsed)
            )
                return Reject(call.Name, "invalid_arguments");
            status = parsed;
        }
        var query = MyCouponsEndpoints.Own(db, context.UserId);
        if (status is { } wanted)
            query = query.Where(coupon => coupon.Status == wanted);
        // No codes: the chat never carries them.
        var coupons = await query
            .OrderByDescending(coupon => coupon.PurchasedAt)
            .ThenByDescending(coupon => coupon.Id)
            .Select(coupon => new CouponSummary(
                coupon.Id,
                coupon.OfferName,
                coupon.Kind.ToString(),
                coupon.Status.ToString(),
                coupon.DiscountPerLiter,
                coupon.MaxLiters
            ))
            .ToListAsync(ct);
        return new(ListMyCoupons, ToolStatus.Ok, coupons);
    }

    /// <summary>Checks the coupon and issues a signed proposal. Changes nothing.</summary>
    private async Task<ToolResult> ProposeAsync(
        ToolCall call,
        ToolContext context,
        CancellationToken ct
    )
    {
        var arguments = StringArguments(call.Arguments, "couponId");
        if (
            arguments is null
            || !arguments.TryGetValue("couponId", out var text)
            || !Guid.TryParseExact(text, "D", out var couponId)
        )
            return Reject(call.Name, "invalid_arguments");

        var coupon = await MyCouponsEndpoints
            .Own(db, context.UserId)
            .Where(c => c.Id == couponId)
            .Select(c => new
            {
                c.Status,
                c.OfferName,
                c.Kind,
                c.DiscountPerLiter,
                c.MaxLiters,
            })
            .SingleOrDefaultAsync(ct);
        // Someone else's and missing coupons look the same.
        if (coupon is null)
            return Reject(call.Name, "coupon_not_found");
        if (coupon.Status != CouponStatus.Purchased)
            return Reject(
                call.Name,
                coupon.Status == CouponStatus.Active ? "coupon_already_active" : "coupon_redeemed"
            );

        var (proposal, token) = tokens.Issue(context.UserId, couponId);
        return new(
            ProposeCouponActivation,
            ToolStatus.Ok,
            new ProposalData(proposal.ProposalId, couponId, coupon.OfferName),
            Confirmation: new ChatConfirmationEvent(
                proposal.ProposalId,
                couponId,
                coupon.OfferName,
                coupon.Kind.ToString(),
                coupon.DiscountPerLiter,
                coupon.MaxLiters,
                proposal.ExpiresAt,
                token
            )
        );
    }
}
