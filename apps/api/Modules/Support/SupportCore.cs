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
    public const string ConversationNotFound = "support.conversation_not_found";
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

    /// <summary>
    /// Origins allowed to connect to the support hub (ADR 0014), e.g. https://app.example (validated
    /// at startup). Empty: no browser can connect to the hub.
    /// </summary>
    public string[] HubAllowedOrigins { get; set; } = [];

    public bool WorkerEnabled { get; set; } = true;
    public TimeSpan WorkerInterval { get; set; } = TimeSpan.FromMinutes(1);

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
        // Compared with the browser's Origin as is: scheme://host[:port], nothing more.
        foreach (var origin in o.HubAllowedOrigins)
            if (
                !Uri.TryCreate(origin, UriKind.Absolute, out var uri)
                || uri.Scheme is not ("http" or "https")
                || origin != uri.GetLeftPart(UriPartial.Authority)
            )
                errors.Add(
                    $"Support:HubAllowedOrigins entry '{origin}' must be scheme://host[:port] without a path or a trailing slash."
                );
        if (o.WorkerInterval < TimeSpan.FromSeconds(1) || o.WorkerInterval > TimeSpan.FromHours(1))
            errors.Add("Support:WorkerInterval must be 1 s - 1 h.");
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
    /// past seq N never misses a later message. Also marks activity. For callers that changed the
    /// case state in this transaction already (claim, release, close, the worker).
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
        return await InsertAsync(db, caseId, last, messages, cancellationToken) is not null;
    }

    /// <summary>
    /// The customer's message, only into their own open case: the ownership and state check and the
    /// sequence number are one statement, so a case closed meanwhile takes no message.
    /// </summary>
    /// <returns>The message's Seq, or null when the case is not the customer's open case.</returns>
    public static async Task<int?> AppendFromCustomerAsync(
        AppDbContext db,
        Guid caseId,
        string userId,
        string text,
        CancellationToken cancellationToken
    )
    {
        var last = await db
            .Database.SqlQuery<int>(
                $"""
                UPDATE "SupportCases"
                SET "LastSeq" = "LastSeq" + 1, "LastActivityAt" = clock_timestamp()
                WHERE "Id" = {caseId} AND "UserId" = {userId} AND "Status" IN ('Waiting', 'Assigned')
                RETURNING "LastSeq" AS "Value"
                """
            )
            .ToListAsync(cancellationToken);
        return await InsertAsync(
            db,
            caseId,
            last,
            [new NewSupportMessage(SupportAuthor.Customer, text)],
            cancellationToken
        );
    }

    /// <summary>
    /// An agent's message, only while that agent is the assignee of the open case (ADR 0012): the
    /// check and the sequence number are one statement under the row lock, so after a release, a
    /// claim by someone else, a close or the idle timeout the former assignee writes nothing.
    /// </summary>
    /// <returns>The message's Seq, or null when the agent is not the assignee of the open case.</returns>
    public static async Task<int?> AppendFromAgentAsync(
        AppDbContext db,
        Guid caseId,
        string agentId,
        string text,
        CancellationToken cancellationToken
    )
    {
        var last = await db
            .Database.SqlQuery<int>(
                $"""
                UPDATE "SupportCases"
                SET "LastSeq" = "LastSeq" + 1, "LastActivityAt" = clock_timestamp(),
                    "AgentActivityAt" = clock_timestamp()
                WHERE "Id" = {caseId} AND "Status" = 'Assigned' AND "AssignedToUserId" = {agentId}
                RETURNING "LastSeq" AS "Value"
                """
            )
            .ToListAsync(cancellationToken);
        return await InsertAsync(
            db,
            caseId,
            last,
            [new NewSupportMessage(SupportAuthor.Support, text, AuthorUserId: agentId)],
            cancellationToken
        );
    }

    private static async Task<int?> InsertAsync(
        AppDbContext db,
        Guid caseId,
        List<int> last,
        IReadOnlyList<NewSupportMessage> messages,
        CancellationToken cancellationToken
    )
    {
        if (last.Count == 0)
            return null;
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
        return last[0];
    }

    /// <summary>Messages after the cursor, in sequence order; at most <see cref="SupportRules.PageSize"/>.</summary>
    public static async Task<List<SupportMessageItem>> PageAsync(
        AppDbContext db,
        Guid caseId,
        int after,
        CancellationToken cancellationToken
    ) =>
        (
            await db
                .SupportMessages.AsNoTracking()
                .Where(m => m.CaseId == caseId && m.Seq > after)
                .OrderBy(m => m.Seq)
                .Take(SupportRules.PageSize)
                .ToListAsync(cancellationToken)
        )
            .Select(m => new SupportMessageItem(
                m.Seq,
                m.Author.ToString(),
                m.ClientProvidedHistory,
                m.HistoryRole,
                m.Text,
                m.CreatedAt
            ))
            .ToList();

    public static List<SupportValidationError> ValidateText(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
            return [new("text", SupportErrorCodes.RequiredField)];
        if (trimmed.Length > SupportRules.MessageMaxLength)
            return [new("text", SupportErrorCodes.TooLong)];
        return [];
    }
}

/// <summary>
/// One message. <c>ClientProvidedHistory</c> marks the chat history the customer attached: untrusted
/// text, shown apart. A System message's text is an event code (claimed, released, ...).
/// </summary>
public sealed record SupportMessageItem(
    int Seq,
    string Author,
    bool ClientProvidedHistory,
    string? HistoryRole,
    string Text,
    DateTimeOffset CreatedAt
);

/// <summary>
/// The case state with the messages after the cursor. Read again with <c>after</c> = the last
/// <c>Seq</c> received: the same cursor gives the same messages, none skipped or repeated.
/// </summary>
public sealed record SupportMessagesResponse(
    Guid CaseId,
    string Status,
    int LastSeq,
    IReadOnlyList<SupportMessageItem> Items
);

public sealed record SupportMessageRequest(string? Text);

/// <summary>The stored message: its <c>Seq</c> lets the client continue the cursor past it.</summary>
public sealed record SupportMessageCreated(int Seq);
