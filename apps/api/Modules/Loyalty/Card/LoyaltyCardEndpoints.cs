using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Loyalty.Card;

public sealed record LoyaltyCardResponse(string Code, DateTimeOffset IssuedAt);

/// <summary>
/// GET reads only. POST issues the first card, POST rotate replaces the active one; both run inside
/// the idempotency transaction (no own transaction here) and serialize per customer with
/// <see cref="CustomerLock"/>; without it two rotations could read the same active card and one would
/// fail on the partial unique index with a 500.
/// </summary>
public static class LoyaltyCardEndpoints
{
    public const string Path = "/api/me/loyalty-card";

    public static async Task<Results<Ok<LoyaltyCardResponse>, ProblemHttpResult>> Get(
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var card = await ActiveCard(db, users.GetUserId(http.User)!)
            .AsNoTracking()
            .Select(c => new LoyaltyCardResponse(c.Code, c.IssuedAt))
            .SingleOrDefaultAsync(cancellationToken);

        return card is null ? LoyaltyProblems.CardMissing() : TypedResults.Ok(card);
    }

    public static async Task<Results<Created<LoyaltyCardResponse>, ProblemHttpResult>> Issue(
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CustomerLock customerLock,
        CancellationToken cancellationToken
    )
    {
        var userId = users.GetUserId(http.User)!;
        if (!await customerLock.TryAcquireAsync(userId, cancellationToken))
            return LoyaltyProblems.CustomerBusy(http);

        if (await ActiveCard(db, userId).AnyAsync(cancellationToken))
            return ApiProblems.Problem(StatusCodes.Status409Conflict, "Loyalty card already issued", LoyaltyErrorCodes.CardAlreadyIssued);

        var card = await AddCardAsync(db, userId, cancellationToken);
        return TypedResults.Created(Path, new LoyaltyCardResponse(card.Code, card.IssuedAt));
    }

    public static async Task<Results<Ok<LoyaltyCardResponse>, ProblemHttpResult>> Rotate(
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CustomerLock customerLock,
        CancellationToken cancellationToken
    )
    {
        var userId = users.GetUserId(http.User)!;
        if (!await customerLock.TryAcquireAsync(userId, cancellationToken))
            return LoyaltyProblems.CustomerBusy(http);

        var current = await ActiveCard(db, userId).SingleOrDefaultAsync(cancellationToken);
        if (current is null)
            return LoyaltyProblems.CardMissing();

        // The old code stops working in the same commit that activates the new one.
        current.RevokedAt = DateTimeOffset.UtcNow;
        await db.SaveChangesAsync(cancellationToken);
        var card = await AddCardAsync(db, userId, cancellationToken);
        return TypedResults.Ok(new LoyaltyCardResponse(card.Code, card.IssuedAt));
    }

    private static IQueryable<LoyaltyCard> ActiveCard(AppDbContext db, string userId) =>
        db.LoyaltyCards.Where(card => card.UserId == userId && card.RevokedAt == null);

    private static async Task<LoyaltyCard> AddCardAsync(
        AppDbContext db,
        string userId,
        CancellationToken cancellationToken
    )
    {
        // 128 random bits: a collision is not a practical concern; UNIQUE (Code) is the last line.
        var card = new LoyaltyCard
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            Code = LoyaltyCardCode.Generate(),
            IssuedAt = DateTimeOffset.UtcNow,
        };
        db.LoyaltyCards.Add(card);
        await db.SaveChangesAsync(cancellationToken);
        return card;
    }
}
