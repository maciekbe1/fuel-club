using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Assistant;
using FuelClub.Api.Modules.Loyalty;
using FuelClub.Api.Modules.Loyalty.Infrastructure;
using FuelClub.Api.Modules.Support.Domain;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Support.Customer;

public sealed record SupportHistoryItem(string? Role, string? Text);

/// <summary><c>History</c> only when the customer ticked "attach the conversation"; it stays untrusted text.</summary>
public sealed record CreateSupportCaseRequest(
    string? Message,
    IReadOnlyList<SupportHistoryItem>? History
);

public sealed record SupportCaseResponse(
    Guid Id,
    string Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? ClosedAt
);

/// <summary>
/// Opens a case for the session user (ADR 0012). Under the customer lock, which chat activations take
/// too: an activation either commits before the case or sees it and is refused. One open case per
/// customer - checked here, enforced by a partial UNIQUE index.
/// </summary>
public static class CreateCaseEndpoint
{
    public const string RateLimitPolicy = "support-cases";

    public const string Description =
        "Opens a support case with the customer's first message and, when the customer chose to attach it, "
        + "the chat history (stored as untrusted client-provided text). While a case is open the chat and its "
        + "confirmations return 409 assistant.handoff_active. An open case already exists -> 409 "
        + "support.case_open with its caseId.";

    public static async Task<Results<Created<SupportCaseResponse>, ProblemHttpResult>> Handle(
        CreateSupportCaseRequest request,
        HttpContext http,
        UserManager<ApplicationUser> users,
        AppDbContext db,
        CustomerLock customerLock,
        IOptions<AssistantOptions> assistant,
        CancellationToken cancellationToken
    )
    {
        var errors = Validate(request, assistant.Value);
        if (errors.Count > 0)
            return SupportProblems.Validation(errors);

        var userId = users.GetUserId(http.User)!;
        if (!await customerLock.TryAcquireAsync(userId, cancellationToken))
            return LoyaltyProblems.CustomerBusy(http);

        var open = await db
            .SupportCases.AsNoTracking()
            .Where(c => c.UserId == userId && SupportCases.Open.Contains(c.Status))
            .Select(c => (Guid?)c.Id)
            .SingleOrDefaultAsync(cancellationToken);
        if (open is { } openId)
            return SupportProblems.CaseOpen(openId);

        var supportCase = new SupportCase
        {
            Id = Guid.CreateVersion7(),
            UserId = userId,
            Status = SupportCaseStatus.Waiting,
        };
        db.SupportCases.Add(supportCase);
        // The partial UNIQUE index is the last line; under the customer lock it is never hit.
        await db.SaveChangesAsync(cancellationToken);

        var messages = (request.History ?? [])
            .Select(item => new NewSupportMessage(
                SupportAuthor.Customer,
                item!.Text!.Trim(),
                ClientProvidedHistory: true,
                HistoryRole: item.Role
            ))
            .Append(new NewSupportMessage(SupportAuthor.Customer, request.Message!.Trim()))
            .ToList();
        await SupportCases.AppendAsync(db, supportCase.Id, messages, cancellationToken);

        var created = await db
            .SupportCases.AsNoTracking()
            .SingleAsync(c => c.Id == supportCase.Id, cancellationToken);
        return TypedResults.Created((string?)null, ToResponse(created));
    }

    public static SupportCaseResponse ToResponse(SupportCase c) =>
        new(c.Id, c.Status.ToString(), c.CreatedAt, c.ClosedAt);

    private static List<SupportValidationError> Validate(
        CreateSupportCaseRequest request,
        AssistantOptions limits
    )
    {
        var errors = new List<SupportValidationError>();
        var message = request.Message?.Trim();
        if (string.IsNullOrEmpty(message))
            errors.Add(new("message", SupportErrorCodes.RequiredField));
        else if (message.Length > SupportRules.MessageMaxLength)
            errors.Add(new("message", SupportErrorCodes.TooLong));

        // The same limits as the chat history the client keeps.
        if (request.History is { } history)
        {
            if (history.Count > limits.MaxHistoryItems)
                errors.Add(new("history", SupportErrorCodes.TooLong));
            else if (
                history.Any(item =>
                    item is null
                    || item.Role is not ("user" or "assistant")
                    || string.IsNullOrWhiteSpace(item.Text)
                    || item.Text.Trim().Length
                        > Math.Min(limits.MaxHistoryTextLength, SupportRules.StoredTextMaxLength)
                )
            )
                errors.Add(new("history", SupportErrorCodes.InvalidFormat));
        }
        return errors;
    }
}
