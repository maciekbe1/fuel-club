using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Support.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FuelClub.Api.Modules.Support.Agent;

/// <summary>What an agent sees of a case: the customer's display name, never the e-mail.</summary>
public sealed record AgentCaseResponse(
    Guid Id,
    string Status,
    string CustomerName,
    DateTimeOffset CreatedAt,
    bool AssignedToMe
);

/// <summary>
/// Atomic claim (ADR 0012): a conditional UPDATE from Waiting. Of two agents one wins; the other gets
/// 409 support.case_taken. The winner's retry is 200, so a lost answer is safe to retry.
/// </summary>
public static class ClaimCaseEndpoint
{
    public const string Description =
        "Claims a waiting case for the session agent. Another agent was first -> 409 support.case_taken; a "
        + "repeated claim by the same agent -> 200; closed or expired -> 409 support.case_closed.";

    public static async Task<Results<Ok<AgentCaseResponse>, ProblemHttpResult>> Handle(
        Guid id,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CancellationToken cancellationToken
    )
    {
        var agentId = users.GetUserId(http.User)!;
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        // The losing UPDATE waits for the winner's row lock, then re-checks the condition and matches nothing.
        var claimed = await db.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE "SupportCases"
            SET "Status" = 'Assigned', "AssignedToUserId" = {agentId}
            WHERE "Id" = {id} AND "Status" = 'Waiting'
            """,
            cancellationToken
        );
        if (claimed == 1)
        {
            await SupportCases.AppendAsync(
                db,
                id,
                [new NewSupportMessage(SupportAuthor.System, SupportSystemEvents.Claimed)],
                cancellationToken
            );
            await transaction.CommitAsync(cancellationToken);
        }

        var current = await Rows(db, db.SupportCases.Where(c => c.Id == id))
            .SingleOrDefaultAsync(cancellationToken);
        return current switch
        {
            null => SupportProblems.CaseNotFound(),
            { Status: SupportCaseStatus.Assigned } when current.AssignedToUserId == agentId =>
                TypedResults.Ok(current.ToResponse(agentId)),
            // Waiting again: another agent claimed it and released it before this read.
            { Status: SupportCaseStatus.Assigned or SupportCaseStatus.Waiting } =>
                SupportProblems.CaseTaken(),
            _ => SupportProblems.CaseClosed(),
        };
    }

    internal sealed record CaseRow(
        Guid Id,
        SupportCaseStatus Status,
        string? AssignedToUserId,
        string CustomerName,
        DateTimeOffset CreatedAt
    )
    {
        public AgentCaseResponse ToResponse(string agentId) =>
            new(
                Id,
                Status.ToString(),
                CustomerName,
                CreatedAt,
                Status == SupportCaseStatus.Assigned && AssignedToUserId == agentId
            );
    }

    /// <summary>Filter the cases first: a constructor projection cannot be filtered in SQL.</summary>
    internal static IQueryable<CaseRow> Rows(AppDbContext db, IQueryable<SupportCase> cases) =>
        from c in cases.AsNoTracking()
        join user in db.Users on c.UserId equals user.Id
        select new CaseRow(c.Id, c.Status, c.AssignedToUserId, user.DisplayName, c.CreatedAt);
}
