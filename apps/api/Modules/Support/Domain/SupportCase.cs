namespace FuelClub.Api.Modules.Support.Domain;

/// <summary>Waiting -> Assigned -> Closed, plus Expired; Assigned -> Waiting on release or idleness (ADR 0012).</summary>
public enum SupportCaseStatus
{
    /// <summary>In the queue, no agent.</summary>
    Waiting,

    /// <summary>Claimed by exactly one agent.</summary>
    Assigned,

    /// <summary>Closed by the customer or the assigned agent.</summary>
    Closed,

    /// <summary>Waited too long without activity; nobody answered.</summary>
    Expired,
}

public enum SupportAuthor
{
    Customer,
    Support,
    System,
}

/// <summary>
/// A customer's request to talk to a person. At most one open case (Waiting or Assigned) per customer
/// (partial UNIQUE index). Messages are numbered per case: <see cref="LastSeq"/> is raised by a
/// conditional UPDATE, so the row lock orders writers and the numbers have no gaps.
/// </summary>
public sealed class SupportCase
{
    public Guid Id { get; set; }
    public string UserId { get; set; } = null!;
    public SupportCaseStatus Status { get; set; }

    /// <summary>Set exactly while Assigned; cleared when the case goes back to the queue.</summary>
    public string? AssignedToUserId { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Last message or state change; drives expiry and the idle timeout.</summary>
    public DateTimeOffset LastActivityAt { get; set; }

    /// <summary>
    /// The assignee's last claim or message; set whenever Assigned. Drives the idle timeout, so the
    /// customer's messages cannot keep a case with an absent agent.
    /// </summary>
    public DateTimeOffset? AgentActivityAt { get; set; }

    /// <summary>Set exactly when Closed or Expired; retention counts from here.</summary>
    public DateTimeOffset? ClosedAt { get; set; }

    public int LastSeq { get; set; }
}

/// <summary>
/// One message of a case. Text lives only in the database: never in logs, spans or security events.
/// System messages carry an event code (<see cref="SupportSystemEvents"/>), not prose.
/// </summary>
public sealed class SupportMessage
{
    public Guid Id { get; set; }
    public Guid CaseId { get; set; }
    public int Seq { get; set; }
    public SupportAuthor Author { get; set; }

    /// <summary>The agent who wrote a Support message; null for the customer and the system.</summary>
    public string? AuthorUserId { get; set; }

    /// <summary>
    /// The chat history the customer chose to attach: untrusted text typed or kept by the client, not
    /// a record of what the system did. Always authored by the customer.
    /// </summary>
    public bool ClientProvidedHistory { get; set; }

    /// <summary>"user" or "assistant" as the client labelled it; only for client-provided history.</summary>
    public string? HistoryRole { get; set; }

    public string Text { get; set; } = null!;
    public DateTimeOffset CreatedAt { get; set; }
}

public static class SupportSystemEvents
{
    public const string Claimed = "claimed";
    public const string Released = "released";
    public const string IdleReturned = "idle_returned";
    public const string ClosedByCustomer = "closed_by_customer";
    public const string ClosedBySupport = "closed_by_support";
    public const string Expired = "expired";

    public static readonly string[] All =
    [
        Claimed,
        Released,
        IdleReturned,
        ClosedByCustomer,
        ClosedBySupport,
        Expired,
    ];
}

public static class SupportRules
{
    public const int MessageMaxLength = 1000;

    /// <summary>Messages per read; the client reads on from the last Seq.</summary>
    public const int PageSize = 100;

    /// <summary>A history entry may be as long as in the chat (AssistantOptions.MaxHistoryTextLength).</summary>
    public const int StoredTextMaxLength = 2000;
}
