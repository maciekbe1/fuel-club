using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Support.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelClub.Api.Modules.Support.Infrastructure;

internal sealed class SupportCaseConfiguration : IEntityTypeConfiguration<SupportCase>
{
    public void Configure(EntityTypeBuilder<SupportCase> builder)
    {
        builder.ToTable(
            "SupportCases",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_SupportCases_Status",
                    "\"Status\" IN ('Waiting', 'Assigned', 'Closed', 'Expired')"
                );
                table.HasCheckConstraint(
                    "CK_SupportCases_Assignee",
                    """
                    ("Status" = 'Assigned' AND "AssignedToUserId" IS NOT NULL)
                    OR ("Status" = 'Waiting' AND "AssignedToUserId" IS NULL)
                    OR "Status" IN ('Closed', 'Expired')
                    """
                );
                table.HasCheckConstraint(
                    "CK_SupportCases_ClosedAt",
                    "(\"Status\" IN ('Closed', 'Expired')) = (\"ClosedAt\" IS NOT NULL)"
                );
                table.HasCheckConstraint("CK_SupportCases_LastSeq", "\"LastSeq\" >= 0");
            }
        );

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).ValueGeneratedNever();
        builder.Property(c => c.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(c => c.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(c => c.LastActivityAt).HasDefaultValueSql("now()");

        // The last line behind "one open case per customer": creation also checks it under the customer lock.
        builder
            .HasIndex(c => c.UserId)
            .IsUnique()
            .HasFilter("\"Status\" IN ('Waiting', 'Assigned')")
            .HasDatabaseName("UX_SupportCases_OpenPerUser");
        builder.HasIndex(c => new { c.Status, c.CreatedAt });
        builder.HasIndex(c => c.AssignedToUserId);
        builder.HasIndex(c => c.ClosedAt);

        builder
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(c => c.AssignedToUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class SupportMessageConfiguration : IEntityTypeConfiguration<SupportMessage>
{
    public void Configure(EntityTypeBuilder<SupportMessage> builder)
    {
        var events = string.Join(", ", SupportSystemEvents.All.Select(e => $"'{e}'"));
        builder.ToTable(
            "SupportMessages",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_SupportMessages_Author",
                    "\"Author\" IN ('Customer', 'Support', 'System')"
                );
                table.HasCheckConstraint(
                    "CK_SupportMessages_AuthorUser",
                    "(\"Author\" = 'Support') = (\"AuthorUserId\" IS NOT NULL)"
                );
                // Attached history is the customer's untrusted text: never System or Support.
                table.HasCheckConstraint(
                    "CK_SupportMessages_History",
                    """
                    ("ClientProvidedHistory" AND "Author" = 'Customer' AND "HistoryRole" IN ('user', 'assistant'))
                    OR (NOT "ClientProvidedHistory" AND "HistoryRole" IS NULL)
                    """
                );
                table.HasCheckConstraint(
                    "CK_SupportMessages_Text",
                    $"char_length(\"Text\") BETWEEN 1 AND {SupportRules.StoredTextMaxLength}"
                );
                table.HasCheckConstraint(
                    "CK_SupportMessages_SystemEvent",
                    $"\"Author\" <> 'System' OR \"Text\" IN ({events})"
                );
                table.HasCheckConstraint("CK_SupportMessages_Seq", "\"Seq\" >= 1");
            }
        );

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).ValueGeneratedNever();
        builder.Property(m => m.Author).HasConversion<string>().HasMaxLength(16);
        builder.Property(m => m.HistoryRole).HasMaxLength(16);
        builder.Property(m => m.Text).HasMaxLength(SupportRules.StoredTextMaxLength);
        builder.Property(m => m.CreatedAt).HasDefaultValueSql("clock_timestamp()");
        builder
            .HasIndex(m => new { m.CaseId, m.Seq })
            .IsUnique()
            .HasDatabaseName("UX_SupportMessages_CaseSeq");
        builder.HasIndex(m => m.AuthorUserId);

        builder
            .HasOne<SupportCase>()
            .WithMany()
            .HasForeignKey(m => m.CaseId)
            .OnDelete(DeleteBehavior.Cascade);
        builder
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(m => m.AuthorUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
