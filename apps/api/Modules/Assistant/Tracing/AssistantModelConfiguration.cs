using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelClub.Api.Modules.Assistant.Tracing;

public sealed class AssistantTurnConfiguration : IEntityTypeConfiguration<AssistantTurn>
{
    public void Configure(EntityTypeBuilder<AssistantTurn> builder)
    {
        builder.ToTable(
            "assistant_turns",
            table =>
            {
                table.HasCheckConstraint("ck_assistant_turns_total_ms", "\"TotalMs\" >= 0");
                table.HasCheckConstraint("ck_assistant_turns_tool_calls", "\"ToolCalls\" >= 0");
            }
        );
        builder.HasKey(turn => turn.Id);
        builder.Property(turn => turn.CorrelationId).HasMaxLength(32);
        builder.Property(turn => turn.UserPseudonym).HasMaxLength(64);
        builder.Property(turn => turn.Provider).HasMaxLength(50);
        builder.Property(turn => turn.Model).HasMaxLength(100);
        builder.Property(turn => turn.PromptVersion).HasMaxLength(100);
        builder.Property(turn => turn.ErrorCode).HasMaxLength(100);
        builder.Property(turn => turn.ReviewedBy).HasMaxLength(450);
        builder.Property(turn => turn.Outcome).HasConversion<string>().HasMaxLength(20);
        builder.OwnsMany(
            turn => turn.Tools,
            tools =>
            {
                tools.ToJson();
                tools.Property(tool => tool.Name).HasMaxLength(50);
                tools.Property(tool => tool.Status).HasMaxLength(20);
            }
        );
        builder.HasIndex(turn => turn.CorrelationId).IsUnique();
        // Metrics and retention read by time; per-user anomalies by pseudonym and time.
        builder.HasIndex(turn => turn.StartedAt);
        builder.HasIndex(turn => new { turn.UserPseudonym, turn.StartedAt });
    }
}
