using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Support.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Support.Customer;

/// <summary>
/// The customer ends their case (ADR 0012); the chat with the bot works again. Idempotent in effect:
/// closing a closed or expired case returns its current state and writes nothing.
/// </summary>
public static class CloseCaseEndpoint
{
    public const string Description =
        "Closes the session user's own open case and re-enables the chat. Repeating it returns the current "
        + "state. Someone else's or an unknown case -> 404 support.case_not_found.";

    public static async Task<Results<Ok<SupportCaseResponse>, ProblemHttpResult>> Handle(
        Guid id,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var userId = users.GetUserId(http.User)!;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var closed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "SupportCases"
            SET "Status" = 'Closed', "ClosedAt" = clock_timestamp()
            WHERE "Id" = {id} AND "UserId" = {userId} AND "Status" IN ('Waiting', 'Assigned')
            """,
            cancellationToken
        );
        if (closed == 1)
        {
            await SupportCases.AppendAsync(
                db,
                id,
                [new NewSupportMessage(SupportAuthor.System, SupportSystemEvents.ClosedByCustomer)],
                cancellationToken
            );
            await transaction.CommitAsync(cancellationToken);
        }

        var current = await db
            .SupportCases.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id && c.UserId == userId, cancellationToken);
        return current is null
            ? SupportProblems.CaseNotFound()
            : TypedResults.Ok(CreateCaseEndpoint.ToResponse(current));
    }
}
