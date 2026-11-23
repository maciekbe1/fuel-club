using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Support.Customer;
using FuelClub.Api.Modules.Support.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Support.Agent;

/// <summary>
/// A queue entry. <c>FirstMessage</c> is the customer's own first message (never attached history);
/// shown as plain text. The customer's display name only, never the e-mail.
/// </summary>
public sealed record SupportQueueItem(
    Guid Id,
    string Status,
    string CustomerName,
    DateTimeOffset CreatedAt,
    DateTimeOffset LastActivityAt,
    string FirstMessage
);

public sealed record SupportQueueResponse(
    IReadOnlyList<SupportQueueItem> Waiting,
    IReadOnlyList<SupportQueueItem> Mine
);

/// <summary>
/// The agent's side (ADR 0012). Reading and writing a case needs the assignment; every write checks
/// it in the same statement that changes the case, so a former assignee changes nothing.
/// </summary>
public static class AgentCaseEndpoints
{
    public const string QueueDescription =
        "The cases assigned to the session agent and the oldest waiting cases, at most 100 in all: customer "
        + "display name, times and the customer's first message.";

    public const string MessagesDescription =
        "The case state and its messages after the cursor, for its assignee only (also after it was closed "
        + "while assigned). Another agent, or a case back in the queue -> 403 support.not_assignee.";

    public const string SendDescription =
        "Adds the assignee's message (1-1000 characters). Not the assignee of the open case any more -> 403 "
        + "support.not_assignee; closed or expired -> 409 support.case_closed.";

    public const string ReleaseDescription =
        "Returns the case to the queue: from then on the former assignee gets 403 support.not_assignee. "
        + "Messages stay and the bot stays off.";

    public const string CloseDescription =
        "Closes the case assigned to the session agent; repeating it returns the closed state.";

    public static async Task<Ok<SupportQueueResponse>> Queue(
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var agentId = users.GetUserId(http.User)!;
        var rows = await (
            from c in db.SupportCases.AsNoTracking()
            where
                c.Status == SupportCaseStatus.Waiting
                || (c.Status == SupportCaseStatus.Assigned && c.AssignedToUserId == agentId)
            join user in db.Users on c.UserId equals user.Id
            select new
            {
                c.Id,
                c.Status,
                user.DisplayName,
                c.CreatedAt,
                c.LastActivityAt,
                First = db
                    .SupportMessages.Where(m =>
                        m.CaseId == c.Id
                        && m.Author == SupportAuthor.Customer
                        && !m.ClientProvidedHistory
                    )
                    .OrderBy(m => m.Seq)
                    .Select(m => m.Text)
                    .FirstOrDefault(),
            }
        )
            // Mine first so the limit never hides them; then the oldest waiting ones.
            .OrderBy(r => r.Status == SupportCaseStatus.Waiting)
            .ThenBy(r => r.CreatedAt)
            .ThenBy(r => r.Id)
            .Take(SupportRules.PageSize)
            .ToListAsync(cancellationToken);
        var items = rows.Select(r =>
                (
                    r.Status,
                    Item: new SupportQueueItem(
                        r.Id,
                        r.Status.ToString(),
                        r.DisplayName,
                        r.CreatedAt,
                        r.LastActivityAt,
                        r.First ?? ""
                    )
                )
            )
            .ToList();
        return TypedResults.Ok(
            new SupportQueueResponse(
                items
                    .Where(i => i.Status == SupportCaseStatus.Waiting)
                    .Select(i => i.Item)
                    .ToList(),
                items
                    .Where(i => i.Status == SupportCaseStatus.Assigned)
                    .Select(i => i.Item)
                    .ToList()
            )
        );
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
        var agentId = users.GetUserId(http.User)!;
        var row = await StateAsync(db, id, cancellationToken);
        if (row is null)
            return SupportProblems.CaseNotFound();
        if (row.AssignedToUserId != agentId)
            return SupportProblems.NotAssignee();
        return
            await CustomerCaseEndpoints.ReadAsync(db, id, after ?? 0, cancellationToken) is { } read
            ? TypedResults.Ok(read)
            : SupportProblems.CaseNotFound();
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
        var agentId = users.GetUserId(http.User)!;
        var seq = await SupportCases.AppendFromAgentAsync(
            db,
            id,
            agentId,
            request.Text!.Trim(),
            cancellationToken
        );
        if (seq is { } stored)
            return TypedResults.Created((string?)null, new SupportMessageCreated(stored));
        return Refusal(await StateAsync(db, id, cancellationToken));
    }

    public static async Task<Results<Ok<AgentCaseResponse>, ProblemHttpResult>> Release(
        Guid id,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var agentId = users.GetUserId(http.User)!;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var released = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "SupportCases" SET "Status" = 'Waiting', "AssignedToUserId" = NULL
            WHERE "Id" = {id} AND "Status" = 'Assigned' AND "AssignedToUserId" = {agentId}
            """,
            cancellationToken
        );
        if (released == 0)
            return Refusal(await StateAsync(db, id, cancellationToken));
        await SupportCases.AppendAsync(
            db,
            id,
            [new NewSupportMessage(SupportAuthor.System, SupportSystemEvents.Released)],
            cancellationToken
        );
        await transaction.CommitAsync(cancellationToken);
        return await ViewAsync(db, id, agentId, cancellationToken);
    }

    public static async Task<Results<Ok<AgentCaseResponse>, ProblemHttpResult>> Close(
        Guid id,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var agentId = users.GetUserId(http.User)!;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var closed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "SupportCases" SET "Status" = 'Closed', "ClosedAt" = clock_timestamp()
            WHERE "Id" = {id} AND "Status" = 'Assigned' AND "AssignedToUserId" = {agentId}
            """,
            cancellationToken
        );
        if (closed == 1)
        {
            await SupportCases.AppendAsync(
                db,
                id,
                [new NewSupportMessage(SupportAuthor.System, SupportSystemEvents.ClosedBySupport)],
                cancellationToken
            );
            await transaction.CommitAsync(cancellationToken);
            return await ViewAsync(db, id, agentId, cancellationToken);
        }

        var row = await StateAsync(db, id, cancellationToken);
        // A repeated close (a lost answer) by the agent who held it: the closed state, nothing written.
        if (row is { Status: SupportCaseStatus.Closed } && row.AssignedToUserId == agentId)
            return await ViewAsync(db, id, agentId, cancellationToken);
        return Refusal(row);
    }

    internal sealed record CaseState(SupportCaseStatus Status, string? AssignedToUserId);

    private static Task<CaseState?> StateAsync(
        AppDbContext db,
        Guid id,
        CancellationToken cancellationToken
    ) =>
        db
            .SupportCases.AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CaseState(c.Status, c.AssignedToUserId))
            .SingleOrDefaultAsync(cancellationToken);

    /// <summary>Why a write by this agent changed nothing.</summary>
    private static ProblemHttpResult Refusal(CaseState? row) =>
        row switch
        {
            null => SupportProblems.CaseNotFound(),
            { Status: SupportCaseStatus.Closed or SupportCaseStatus.Expired } =>
                SupportProblems.CaseClosed(),
            _ => SupportProblems.NotAssignee(),
        };

    /// <summary>The case as the agent sees it; 404 when retention deleted it meanwhile.</summary>
    private static async Task<Results<Ok<AgentCaseResponse>, ProblemHttpResult>> ViewAsync(
        AppDbContext db,
        Guid id,
        string agentId,
        CancellationToken cancellationToken
    ) =>
        await ClaimCaseEndpoint
            .Rows(db, db.SupportCases.Where(c => c.Id == id))
            .SingleOrDefaultAsync(cancellationToken)
            is { } row
            ? TypedResults.Ok(row.ToResponse(agentId))
            : SupportProblems.CaseNotFound();
}
