using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Rewards.Admin;

public sealed record AdminRewardResponse(
    Guid Id,
    string Name,
    string Description,
    int PointsCost,
    string Kind,
    decimal? DiscountPerLiter,
    decimal? MaxLiters,
    bool IsActive,
    int Version,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public sealed record AdminRewardsResponse(IReadOnlyList<AdminRewardResponse> Items);

/// <summary>
/// The whole form. Numbers and <c>IsActive</c> are nullable only to report a missing field as
/// required (not as "out of range", and a missing isActive must not deactivate an offer).
/// <c>Kind</c>: FuelDiscount (default) or Product; a product sends no per-liter terms.
/// </summary>
public sealed record CreateRewardRequest(
    string? Name,
    string? Description,
    int? PointsCost,
    string? Kind,
    decimal? DiscountPerLiter,
    decimal? MaxLiters,
    bool? IsActive
);

/// <summary>
/// The whole form plus the version it was loaded with (PUT, also for activate/deactivate).
/// <c>Kind</c> may be sent but must match the offer: it never changes (400 rewards.kind_immutable).
/// </summary>
public sealed record UpdateRewardRequest(
    string? Name,
    string? Description,
    int? PointsCost,
    string? Kind,
    decimal? DiscountPerLiter,
    decimal? MaxLiters,
    bool? IsActive,
    int? Version
);

/// <summary>
/// Offer management for admins. Edits are guarded by an explicit version: one conditional
/// <c>UPDATE ... WHERE Version = @version ... RETURNING</c> applies the change, increments the version
/// and returns the stored row, with no separate read that a concurrent edit could slip between.
/// </summary>
public static class AdminRewardEndpoints
{
    public const string Path = "/api/admin/rewards";

    public const string UpdateDescription =
        "Replaces the whole offer, including isActive. 'version' must be the version the form was loaded with: "
        + "a stale version returns 409 rewards.offer_version_conflict and nothing is saved. Not idempotent: "
        + "after a lost response a retry gets 409 because the first attempt already raised the version, which "
        + "does not prove that someone else changed the offer - reload it.";

    public static async Task<Ok<AdminRewardsResponse>> GetAll(
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var items = await db
            .RewardOffers.AsNoTracking()
            .OrderByDescending(offer => offer.CreatedAt)
            .ThenBy(offer => offer.Id)
            .Select(ToAdmin)
            .ToListAsync(cancellationToken);
        return TypedResults.Ok(new AdminRewardsResponse(items));
    }

    public static async Task<Results<Ok<AdminRewardResponse>, ProblemHttpResult>> GetOne(
        Guid id,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var offer = await db
            .RewardOffers.AsNoTracking()
            .Where(o => o.Id == id)
            .Select(ToAdmin)
            .SingleOrDefaultAsync(cancellationToken);
        return offer is null ? RewardsProblems.OfferNotFound() : TypedResults.Ok(offer);
    }

    /// <summary>Runs inside the idempotency transaction: a retry after a lost response returns the same offer.</summary>
    public static async Task<Results<Created<AdminRewardResponse>, ProblemHttpResult>> Create(
        CreateRewardRequest request,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var kind = ParseKind(request.Kind ?? nameof(RewardKind.FuelDiscount));
        var (input, errors) = ValidateForm(
            request.Name,
            request.Description,
            request.PointsCost,
            kind ?? RewardKind.FuelDiscount,
            request.DiscountPerLiter,
            request.MaxLiters,
            request.IsActive
        );
        if (kind is null)
        {
            // Unknown kind: per-liter errors would point at fields a product must not have.
            errors.RemoveAll(error => error.Field is "discountPerLiter" or "maxLiters");
            errors.Add(new("kind", RewardsErrorCodes.InvalidFormat));
        }
        if (errors.Count > 0)
            return RewardsProblems.Validation("Offer is not valid", errors);

        var offer = new RewardOffer
        {
            Id = Guid.CreateVersion7(),
            Name = input.Name!,
            Description = input.Description!,
            PointsCost = input.PointsCost,
            Kind = input.Kind,
            DiscountPerLiter = input.DiscountPerLiter,
            MaxLiters = input.MaxLiters,
            IsActive = request.IsActive!.Value,
            Version = 1,
        };
        db.RewardOffers.Add(offer);
        await db.SaveChangesAsync(cancellationToken);

        var created = await db
            .RewardOffers.AsNoTracking()
            .Where(o => o.Id == offer.Id)
            .Select(ToAdmin)
            .SingleAsync(cancellationToken);
        return TypedResults.Created($"{Path}/{offer.Id}", created);
    }

    public static async Task<Results<Ok<AdminRewardResponse>, ProblemHttpResult>> Update(
        Guid id,
        UpdateRewardRequest request,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        // The kind is fixed at creation and offers are never deleted, so reading it first is race-free.
        var stored = await db
            .RewardOffers.Where(offer => offer.Id == id)
            .Select(offer => (RewardKind?)offer.Kind)
            .SingleOrDefaultAsync(cancellationToken);
        if (stored is not { } offerKind)
            return RewardsProblems.OfferNotFound();
        var (input, errors) = ValidateForm(
            request.Name,
            request.Description,
            request.PointsCost,
            offerKind,
            request.DiscountPerLiter,
            request.MaxLiters,
            request.IsActive
        );
        if (request.Kind is not null)
        {
            var kind = ParseKind(request.Kind);
            if (kind is null)
                errors.Add(new("kind", RewardsErrorCodes.InvalidFormat));
            else if (kind != offerKind)
                errors.Add(new("kind", RewardsErrorCodes.KindImmutable));
        }
        if (request.Version is null)
            errors.Add(new("version", RewardsErrorCodes.RequiredField));
        else if (request.Version < 1)
            errors.Add(new("version", RewardsErrorCodes.VersionOutOfRange));
        if (errors.Count > 0)
            return RewardsProblems.Validation("Offer is not valid", errors);

        // Single statement: compare the version, apply the form, bump the version, return the stored row.
        var rows = await db
            .Database.SqlQuery<AdminRewardResponse>(
                $"""
                UPDATE "RewardOffers"
                SET "Name" = {input.Name}, "Description" = {input.Description}, "PointsCost" = {input.PointsCost},
                    "DiscountPerLiter" = {input.DiscountPerLiter}, "MaxLiters" = {input.MaxLiters},
                    "IsActive" = {request.IsActive!.Value}, "Version" = "Version" + 1, "UpdatedAt" = now()
                WHERE "Id" = {id} AND "Version" = {request.Version!.Value}
                RETURNING "Id", "Name", "Description", "PointsCost", "Kind", "DiscountPerLiter", "MaxLiters",
                          "IsActive", "Version", "CreatedAt", "UpdatedAt"
                """
            )
            .ToListAsync(cancellationToken);

        if (rows.Count == 1)
            return TypedResults.Ok(rows[0]);

        // Offers are never deleted, so "exists" cannot change between the UPDATE and this check.
        return await db.RewardOffers.AnyAsync(offer => offer.Id == id, cancellationToken)
            ? ApiProblems.Problem(
                StatusCodes.Status409Conflict,
                "Offer was changed since it was loaded",
                RewardsErrorCodes.OfferVersionConflict
            )
            : RewardsProblems.OfferNotFound();
    }

    private static readonly System.Linq.Expressions.Expression<
        Func<RewardOffer, AdminRewardResponse>
    > ToAdmin = offer => new AdminRewardResponse(
        offer.Id,
        offer.Name,
        offer.Description,
        offer.PointsCost,
        offer.Kind.ToString(),
        offer.DiscountPerLiter,
        offer.MaxLiters,
        offer.IsActive,
        offer.Version,
        offer.CreatedAt,
        offer.UpdatedAt
    );

    private static (RewardOfferInput Input, List<RewardsValidationError> Errors) ValidateForm(
        string? name,
        string? description,
        int? pointsCost,
        RewardKind kind,
        decimal? discountPerLiter,
        decimal? maxLiters,
        bool? isActive
    )
    {
        // Missing per-liter terms are reported by the rules (required for a fuel discount only).
        var input = RewardOfferRules.Normalize(
            new RewardOfferInput(
                name,
                description,
                pointsCost ?? 0,
                kind,
                discountPerLiter,
                maxLiters
            )
        );
        var missing = new List<string>();
        if (pointsCost is null)
            missing.Add("pointsCost");
        if (isActive is null)
            missing.Add("isActive");

        // A missing value is reported once, as required, not also as out of range.
        var errors = RewardOfferRules
            .Validate(input)
            .Where(error => !missing.Contains(error.Field))
            .ToList();
        errors.AddRange(
            missing.Select(field => new RewardsValidationError(
                field,
                RewardsErrorCodes.RequiredField
            ))
        );
        return (input, errors);
    }

    private static RewardKind? ParseKind(string value) =>
        value switch
        {
            nameof(RewardKind.FuelDiscount) => RewardKind.FuelDiscount,
            nameof(RewardKind.Product) => RewardKind.Product,
            _ => null,
        };
}
