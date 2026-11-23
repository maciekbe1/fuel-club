using FuelClub.Api.Infrastructure;
using FuelClub.Api.Modules.Assistant.Tracing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace FuelClub.Api.Modules.Assistant.Conversations;

public enum TurnAdmission
{
    Admitted,

    /// <summary>Unknown or someone else's: the same answer, so an id reveals nothing.</summary>
    NotFound,
    Ended,
}

/// <summary>
/// Conversation state (ADR 0017). Every change is one conditional UPDATE: owner, state and the idle
/// limit are checked in the same statement that changes the row, so two turns, a turn and an end,
/// or a retried case link cannot race past each other.
/// </summary>
public sealed class Conversations(
    AppDbContext db,
    TracePseudonyms pseudonyms,
    IOptions<AssistantOptions> options,
    IOptions<SimulationOptions> simulation,
    TimeProvider clock
)
{
    public async Task<AssistantConversation> StartAsync(string userId, CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var run = simulation.Value;
        var conversation = new AssistantConversation
        {
            Id = Guid.CreateVersion7(),
            UserPseudonym = pseudonyms.For(userId),
            StartedAt = now,
            LastActivityAt = now,
            Synthetic = run.Enabled,
            RunId = run.Enabled ? run.RunId : null,
        };
        db.AssistantConversations.Add(conversation);
        await db.SaveChangesAsync(ct);
        return conversation;
    }

    /// <summary>Counts the turn only when the conversation is the user's, open and not idle.</summary>
    public async Task<TurnAdmission> BeginTurnAsync(Guid id, string userId, CancellationToken ct)
    {
        var owner = pseudonyms.For(userId);
        var now = clock.GetUtcNow();
        var activeSince = now - options.Value.ConversationIdleTimeout;
        var admitted = await db
            .AssistantConversations.Where(c =>
                c.Id == id
                && c.UserPseudonym == owner
                && c.EndedAt == null
                && c.LastActivityAt >= activeSince
            )
            .ExecuteUpdateAsync(
                set =>
                    set.SetProperty(c => c.Turns, c => c.Turns + 1)
                        .SetProperty(c => c.LastActivityAt, now),
                ct
            );
        if (admitted == 1)
            return TurnAdmission.Admitted;
        if (
            !await db.AssistantConversations.AnyAsync(
                c => c.Id == id && c.UserPseudonym == owner,
                ct
            )
        )
            return TurnAdmission.NotFound;
        await CloseIdleAsync(id, owner, now, ct);
        return TurnAdmission.Ended;
    }

    /// <summary>
    /// Idempotent: an ended conversation keeps its first reason and time. One left idle ends as
    /// Idle, not Client - the customer did not end it now. Null when it is not the user's.
    /// </summary>
    public async Task<AssistantConversation?> EndAsync(Guid id, string userId, CancellationToken ct)
    {
        var owner = pseudonyms.For(userId);
        var now = clock.GetUtcNow();
        await CloseIdleAsync(id, owner, now, ct);
        await db
            .AssistantConversations.Where(c =>
                c.Id == id && c.UserPseudonym == owner && c.EndedAt == null
            )
            .ExecuteUpdateAsync(
                set =>
                    set.SetProperty(c => c.EndedAt, now)
                        .SetProperty(c => c.EndReason, ConversationEndReason.Client),
                ct
            );
        return await db
            .AssistantConversations.AsNoTracking()
            .SingleOrDefaultAsync(c => c.Id == id && c.UserPseudonym == owner, ct);
    }

    /// <summary>
    /// Links the support case once (handoff counted once per conversation): a retry or a second
    /// case leaves the first link. Ends the conversation as Handoff unless it already ended.
    /// False when the conversation is not the user's.
    /// </summary>
    public async Task<bool> LinkCaseAsync(Guid id, string userId, Guid caseId, CancellationToken ct)
    {
        var owner = pseudonyms.For(userId);
        var now = clock.GetUtcNow();
        await db
            .AssistantConversations.Where(c =>
                c.Id == id && c.UserPseudonym == owner && c.SupportCaseId == null
            )
            .ExecuteUpdateAsync(
                set =>
                    set.SetProperty(c => c.SupportCaseId, caseId)
                        .SetProperty(c => c.EndedAt, c => c.EndedAt ?? now)
                        .SetProperty(
                            c => c.EndReason,
                            c => c.EndReason ?? ConversationEndReason.Handoff
                        ),
                ct
            );
        return await db.AssistantConversations.AnyAsync(
            c => c.Id == id && c.UserPseudonym == owner,
            ct
        );
    }

    public async Task<bool> IsOwnAsync(Guid id, string userId, CancellationToken ct)
    {
        var owner = pseudonyms.For(userId);
        return await db.AssistantConversations.AnyAsync(
            c => c.Id == id && c.UserPseudonym == owner,
            ct
        );
    }

    private Task<int> CloseIdleAsync(
        Guid id,
        string owner,
        DateTimeOffset now,
        CancellationToken ct
    )
    {
        var idle = options.Value.ConversationIdleTimeout;
        var activeSince = now - idle;
        return db
            .AssistantConversations.Where(c =>
                c.Id == id
                && c.UserPseudonym == owner
                && c.EndedAt == null
                && c.LastActivityAt < activeSince
            )
            .ExecuteUpdateAsync(
                set =>
                    // Ended when the idle limit ran out, not when someone noticed.
                    set.SetProperty(c => c.EndedAt, c => c.LastActivityAt + idle)
                        .SetProperty(c => c.EndReason, ConversationEndReason.Idle),
                ct
            );
    }
}
