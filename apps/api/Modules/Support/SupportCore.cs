using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Support.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Support;

public static class SupportErrorCodes
{
    public const string ValidationFailed = "support.validation_failed";
    public const string RequiredField = "support.required_field";
    public const string TooLong = "support.too_long";
    public const string InvalidFormat = "support.invalid_format";
    public const string CaseOpen = "support.case_open";
    public const string CaseNotFound = "support.case_not_found";
    public const string CaseTaken = "support.case_taken";
    public const string CaseClosed = "support.case_closed";
    public const string NotAssignee = "support.not_assignee";
}

public sealed record SupportValidationError(string Field, string Code) : IFieldError;

/// <summary>
/// The 409 of case creation in OpenAPI: support.case_open carries the open case's id. Documentation
/// only - the response is ProblemDetails with this extension.
/// </summary>
public sealed record SupportConflictProblem(
    string Code,
    string? Type = null,
    string? Title = null,
    int? Status = null,
    Guid? CaseId = null
);

internal static class SupportProblems
{
    public static ProblemHttpResult Validation(IEnumerable<SupportValidationError> errors) =>
        ApiProblems.Validation(
            "Support request is not valid",
            SupportErrorCodes.ValidationFailed,
            errors
        );

    public static ProblemHttpResult CaseOpen(Guid caseId) =>
        TypedResults.Problem(
            statusCode: StatusCodes.Status409Conflict,
            title: "A support case is already open",
            extensions: new Dictionary<string, object?>
            {
                ["code"] = SupportErrorCodes.CaseOpen,
                ["caseId"] = caseId,
            }
        );

    /// <summary>Also for another customer's case: its existence is not revealed.</summary>
    public static ProblemHttpResult CaseNotFound() =>
        ApiProblems.Problem(
            StatusCodes.Status404NotFound,
            "Support case not found",
            SupportErrorCodes.CaseNotFound
        );

    public static ProblemHttpResult CaseTaken() =>
        ApiProblems.Problem(
            StatusCodes.Status409Conflict,
            "Support case claimed by another agent",
            SupportErrorCodes.CaseTaken
        );

    public static ProblemHttpResult CaseClosed() =>
        ApiProblems.Problem(
            StatusCodes.Status409Conflict,
            "Support case is closed",
            SupportErrorCodes.CaseClosed
        );

    public static ProblemHttpResult NotAssignee() =>
        ApiProblems.Problem(
            StatusCodes.Status403Forbidden,
            "Support case is assigned to another agent",
            SupportErrorCodes.NotAssignee
        );
}

public sealed class SupportOptions
{
    public const string Section = "Support";

    /// <summary>A waiting case without activity this long expires (nobody answered).</summary>
    public TimeSpan WaitingExpiry { get; set; } = TimeSpan.FromDays(7);

    /// <summary>An assigned case without a message this long goes back to the queue.</summary>
    public TimeSpan AssignedIdleTimeout { get; set; } = TimeSpan.FromHours(24);

    /// <summary>A closed or expired case and its messages are deleted this long after closing.</summary>
    public TimeSpan Retention { get; set; } = TimeSpan.FromDays(30);

    public int CasePermitLimit { get; set; } = 5;
    public TimeSpan CaseWindow { get; set; } = TimeSpan.FromHours(1);
    public int MessagePermitLimit { get; set; } = 30;
    public TimeSpan MessageWindow { get; set; } = TimeSpan.FromMinutes(1);
}

public sealed class SupportOptionsValidator : IValidateOptions<SupportOptions>
{
    public ValidateOptionsResult Validate(string? name, SupportOptions o)
    {
        var errors = new List<string>();
        if (o.WaitingExpiry <= TimeSpan.Zero)
            errors.Add("Support:WaitingExpiry must be positive.");
        if (o.AssignedIdleTimeout <= TimeSpan.Zero)
            errors.Add("Support:AssignedIdleTimeout must be positive.");
        if (o.Retention <= TimeSpan.Zero)
            errors.Add("Support:Retention must be positive.");
        if (o.CasePermitLimit < 1 || o.MessagePermitLimit < 1)
            errors.Add("Support permit limits must be at least 1.");
        if (o.CaseWindow <= TimeSpan.Zero || o.MessageWindow <= TimeSpan.Zero)
            errors.Add("Support rate limit windows must be positive.");
        return errors.Count == 0
            ? ValidateOptionsResult.Success
            : ValidateOptionsResult.Fail(errors);
    }
}

public sealed record NewSupportMessage(
    SupportAuthor Author,
    string Text,
    string? AuthorUserId = null,
    bool ClientProvidedHistory = false,
    string? HistoryRole = null
);

/// <summary>Shared case operations; callers run them inside a transaction.</summary>
public static class SupportCases
{
    public static readonly SupportCaseStatus[] Open =
    [
        SupportCaseStatus.Waiting,
        SupportCaseStatus.Assigned,
    ];

    /// <summary>
    /// The guard of the chat and its confirmations (ADR 0012). For a decision that must not race with
    /// case creation, call it under the customer lock: creation takes the same lock.
    /// </summary>
    public static Task<bool> HasOpenCaseAsync(
        AppDbContext db,
        string userId,
        CancellationToken cancellationToken
    ) =>
        db.SupportCases.AnyAsync(
            c => c.UserId == userId && Open.Contains(c.Status),
            cancellationToken
        );

    /// <summary>
    /// Appends messages with the next sequence numbers. The UPDATE locks the case row until commit, so
    /// concurrent writers of one case number their messages in commit order without gaps; a reader
    /// past seq N never misses a later message. Also marks activity.
    /// </summary>
    /// <returns>false when the case does not exist; nothing is written.</returns>
    public static async Task<bool> AppendAsync(
        AppDbContext db,
        Guid caseId,
        IReadOnlyList<NewSupportMessage> messages,
        CancellationToken cancellationToken
    )
    {
        if (messages.Count == 0)
            return true;
        var last = await db
            .Database.SqlQuery<int>(
                $"""
                UPDATE "SupportCases"
                SET "LastSeq" = "LastSeq" + {messages.Count}, "LastActivityAt" = clock_timestamp()
                WHERE "Id" = {caseId}
                RETURNING "LastSeq" AS "Value"
                """
            )
            .ToListAsync(cancellationToken);
        if (last.Count == 0)
            return false;
        var seq = last[0] - messages.Count;
        foreach (var message in messages)
            db.SupportMessages.Add(
                new SupportMessage
                {
                    Id = Guid.CreateVersion7(),
                    CaseId = caseId,
                    Seq = ++seq,
                    Author = message.Author,
                    AuthorUserId = message.AuthorUserId,
                    ClientProvidedHistory = message.ClientProvidedHistory,
                    HistoryRole = message.HistoryRole,
                    Text = message.Text,
                }
            );
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }
}
