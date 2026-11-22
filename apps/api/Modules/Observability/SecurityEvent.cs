using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelClub.Api.Modules.Observability;

public enum SecurityEventKind
{
    /// <summary>403: wrong role, CSRF rejected, staff on customer data.</summary>
    AccessDenied,

    /// <summary>401 from the login endpoint (wrong password, unknown e-mail or locked out).</summary>
    LoginFailed,

    /// <summary>409 idempotency.key_reused: the same key with a different request.</summary>
    IdempotencyConflict,

    /// <summary>409 rewards.coupon_redeemed: a coupon presented again after its redemption.</summary>
    RepeatedRedemption,

    /// <summary>429 from any rate limit.</summary>
    RateLimited,
}

/// <summary>
/// A rejected request worth counting. Metadata only: the route pattern (never the URL with ids),
/// the status, the error code and, for a signed-in caller, the pseudonym. No IP, body or headers.
/// </summary>
public sealed class SecurityEvent
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; }
    public SecurityEventKind Kind { get; set; }
    public int Status { get; set; }
    public string? Code { get; set; }
    public required string Route { get; set; }
    public string? UserPseudonym { get; set; }
}

public sealed class SecurityEventConfiguration : IEntityTypeConfiguration<SecurityEvent>
{
    public void Configure(EntityTypeBuilder<SecurityEvent> builder)
    {
        builder.ToTable("security_events");
        builder.HasKey(e => e.Id);
        builder.Property(e => e.Kind).HasConversion<string>().HasMaxLength(40);
        builder.Property(e => e.Code).HasMaxLength(100);
        builder.Property(e => e.Route).HasMaxLength(200);
        builder.Property(e => e.UserPseudonym).HasMaxLength(64);
        builder.HasIndex(e => new { e.Kind, e.OccurredAt });
        builder.HasIndex(e => e.OccurredAt);
    }
}
