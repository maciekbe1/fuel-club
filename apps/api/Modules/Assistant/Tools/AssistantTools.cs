using System.Text.Json;
using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Assistant.Chat;
using FuelClub.Api.Modules.Assistant.Confirmation;
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
/// propose_coupon_activation and goes to the client as an event, not to the model.
/// </summary>
public sealed record ToolResult(
    string Name,
    string Status,
    object? Data = null,
    string? Reason = null,
    ChatConfirmationEvent? Confirmation = null
);

public sealed record BalanceData(int Balance);

public sealed record RewardSummary(
    string Name,
    int PointsCost,
    decimal DiscountPerLiter,
    decimal MaxLiters
);

public sealed record CouponSummary(
    Guid Id,
    string OfferName,
    string Status,
    decimal DiscountPerLiter,
    decimal MaxLiters
);

public sealed record ProposalData(Guid ProposalId, Guid CouponId, string OfferName);

/// <summary>
/// The closed list of assistant tools. Every tool reads the session user's data only; none takes a
/// user argument, none mutates. Unknown tools and bad arguments are rejected without running.
/// </summary>
public sealed class AssistantTools(AppDbContext db, ConfirmationTokens tokens)
{
    public const string GetMyBalance = "get_my_balance";
    public const string ListAvailableRewards = "list_available_rewards";
    public const string ListMyCoupons = "list_my_coupons";
    public const string ProposeCouponActivation = "propose_coupon_activation";

    public static readonly IReadOnlyList<string> Names =
    [
        GetMyBalance,
        ListAvailableRewards,
        ListMyCoupons,
        ProposeCouponActivation,
    ];

    public Task<ToolResult> ExecuteAsync(
        ToolCall call,
        ToolContext context,
        CancellationToken cancellationToken
    ) =>
        call.Name switch
        {
            GetMyBalance => NoArguments(call) ?? BalanceAsync(context, cancellationToken),
            ListAvailableRewards => NoArguments(call) ?? RewardsAsync(cancellationToken),
            ListMyCoupons => CouponsAsync(call, context, cancellationToken),
            ProposeCouponActivation => ProposeAsync(call, context, cancellationToken),
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

    private async Task<ToolResult> RewardsAsync(CancellationToken ct)
    {
        var offers = await CatalogEndpoint
            .ActiveOffers(db)
            .Select(offer => new RewardSummary(
                offer.Name,
                offer.PointsCost,
                offer.DiscountPerLiter,
                offer.MaxLiters
            ))
            .ToListAsync(ct);
        return new(ListAvailableRewards, ToolStatus.Ok, offers);
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
                coupon.DiscountPerLiter,
                coupon.MaxLiters,
                proposal.ExpiresAt,
                token
            )
        );
    }
}
