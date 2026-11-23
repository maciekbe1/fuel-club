using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Support.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Support.Customer;

/// <summary>The customer's side of their own cases; someone else's case looks exactly like a missing one.</summary>
public static class CustomerCaseEndpoints
{
    public const string MessageRateLimitPolicy = "support-messages";

    public const string CurrentDescription =
        "The session user's open case (Waiting or Assigned), or 204 without one. The chat window calls it "
        + "on entry, so a case survives a reload and a new login.";

    public const string MessagesDescription =
        "The case state and its messages after the cursor (after = the last seq received, 0 at first), at "
        + "most 100 in seq order. Poll every 3 s while the case is open. Someone else's case -> 404.";

    public const string SendDescription =
        "Adds the customer's message (1-1000 characters) to their open case. Closed or expired -> 409 "
        + "support.case_closed.";

    public static async Task<Results<Ok<SupportCaseResponse>, NoContent>> Current(
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var userId = users.GetUserId(http.User)!;
        var open = await db
            .SupportCases.AsNoTracking()
            .Where(c => c.UserId == userId && SupportCases.Open.Contains(c.Status))
            .SingleOrDefaultAsync(cancellationToken);
        return open is null
            ? TypedResults.NoContent()
            : TypedResults.Ok(CreateCaseEndpoint.ToResponse(open));
    }

    public static async Task<Results<Ok<SupportMessagesResponse>, ProblemHttpResult>> Messages(
        Guid id,
        int? after,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        if (after is < 0)
            return SupportProblems.Validation([new("after", SupportErrorCodes.InvalidFormat)]);
        var userId = users.GetUserId(http.User)!;
        if (
            !await db.SupportCases.AnyAsync(
                c => c.Id == id && c.UserId == userId,
                cancellationToken
            )
        )
            return SupportProblems.CaseNotFound();
        return await ReadAsync(db, id, after ?? 0, cancellationToken) is { } read
            ? TypedResults.Ok(read)
            : SupportProblems.CaseNotFound();
    }

    /// <summary>
    /// Messages first, then the state: the returned LastSeq is never below the last item. Null when
    /// the case was deleted (retention) after the caller's access check.
    /// </summary>
    internal static async Task<SupportMessagesResponse?> ReadAsync(
        AppDbContext db,
        Guid id,
        int after,
        CancellationToken cancellationToken
    )
    {
        var items = await SupportCases.PageAsync(db, id, after, cancellationToken);
        var state = await db
            .SupportCases.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new { c.Status, c.LastSeq })
            .SingleOrDefaultAsync(cancellationToken);
        return state is null
            ? null
            : new SupportMessagesResponse(id, state.Status.ToString(), state.LastSeq, items);
    }

    public static async Task<Results<Created<SupportMessageCreated>, ProblemHttpResult>> Send(
        Guid id,
        SupportMessageRequest request,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var errors = SupportCases.ValidateText(request.Text);
        if (errors.Count > 0)
            return SupportProblems.Validation(errors);
        var userId = users.GetUserId(http.User)!;
        var seq = await SupportCases.AppendFromCustomerAsync(
            db,
            id,
            userId,
            request.Text!.Trim(),
            cancellationToken
        );
        if (seq is { } stored)
            return TypedResults.Created((string?)null, new SupportMessageCreated(stored));

        var status = await db
            .SupportCases.AsNoTracking()
            .Where(c => c.Id == id && c.UserId == userId)
            .Select(c => (SupportCaseStatus?)c.Status)
            .SingleOrDefaultAsync(cancellationToken);
        return status is null ? SupportProblems.CaseNotFound() : SupportProblems.CaseClosed();
    }
}
