using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelClub.Api.Modules.Assistant.Conversations;

/// <summary>
/// A chat conversation (ADR 0017): metadata only, like the turns (ADR 0008). The owner is the
/// trace pseudonym, never the user id. The server creates it before the first model call; a
/// support case links to it at most once.
/// </summary>
public sealed class AssistantConversation
{
    public Guid Id { get; set; }
    public required string UserPseudonym { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset LastActivityAt { get; set; }
    public DateTimeOffset? EndedAt { get; set; }
    public ConversationEndReason? EndReason { get; set; }
    public int Turns { get; set; }

    /// <summary>The case opened from this conversation; the first link wins, retries do not add.</summary>
    public Guid? SupportCaseId { get; set; }

    /// <summary>From the instance configuration (Simulation), never from a request.</summary>
    public bool Synthetic { get; set; }
    public string? RunId { get; set; }
}

/// <summary>
/// Why it ended. None of them means the matter was resolved: Idle is a timeout, Client is the
/// customer leaving or starting over, Handoff is a case opened.
/// </summary>
public enum ConversationEndReason
{
    Client,
    Idle,
    Handoff,
}

public sealed class AssistantConversationConfiguration
    : IEntityTypeConfiguration<AssistantConversation>
{
    public void Configure(EntityTypeBuilder<AssistantConversation> builder)
    {
        builder.ToTable(
            "assistant_conversations",
            table =>
            {
                table.HasCheckConstraint("ck_assistant_conversations_turns", "\"Turns\" >= 0");
                table.HasCheckConstraint(
                    "ck_assistant_conversations_end",
                    "(\"EndedAt\" IS NULL) = (\"EndReason\" IS NULL)"
                );
                table.HasCheckConstraint(
                    "ck_assistant_conversations_run",
                    "\"RunId\" IS NULL OR \"Synthetic\""
                );
            }
        );
        builder.HasKey(c => c.Id);
        builder.Property(c => c.UserPseudonym).HasMaxLength(64);
        builder.Property(c => c.EndReason).HasConversion<string>().HasMaxLength(20);
        builder.Property(c => c.RunId).HasMaxLength(40);
        // No foreign key to the case: cases are purged on their own retention (Support).
        builder.HasIndex(c => c.SupportCaseId).IsUnique();
        builder.HasIndex(c => c.StartedAt);
        builder.HasIndex(c => new { c.UserPseudonym, c.StartedAt });
    }
}
