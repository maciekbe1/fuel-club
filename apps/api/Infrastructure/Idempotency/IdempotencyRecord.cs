using FuelClub.Api.Modules.Accounts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelClub.Api.Infrastructure.Idempotency;

/// <summary>
/// One idempotent operation of one user. The row is inserted (reserving the key) and completed with the
/// response in the same transaction, so a committed row always carries a stored response.
/// </summary>
public sealed class IdempotencyRecord
{
    public const int FingerprintLength = 32;
    public const int ContentTypeMaxLength = 256;
    public const int LocationMaxLength = 2048;

    public string UserId { get; set; } = null!;
    public Guid IdempotencyKey { get; set; }
    public byte[] Fingerprint { get; set; } = null!;
    public int? StatusCode { get; set; }
    public string? ContentType { get; set; }
    public string? Location { get; set; }
    public byte[]? Body { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

internal sealed class IdempotencyRecordConfiguration : IEntityTypeConfiguration<IdempotencyRecord>
{
    public void Configure(EntityTypeBuilder<IdempotencyRecord> builder)
    {
        builder.ToTable(
            "IdempotencyRecords",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_IdempotencyRecords_Fingerprint_Length",
                    $"octet_length(\"Fingerprint\") = {IdempotencyRecord.FingerprintLength}"
                );
                // Only final outcomes are stored: 2xx and selected 4xx. 5xx are never committed.
                table.HasCheckConstraint(
                    "CK_IdempotencyRecords_StatusCode_Range",
                    "\"StatusCode\" BETWEEN 200 AND 499"
                );
            }
        );

        builder.HasKey(record => new { record.UserId, record.IdempotencyKey });
        builder
            .Property(record => record.ContentType)
            .HasMaxLength(IdempotencyRecord.ContentTypeMaxLength);
        builder
            .Property(record => record.Location)
            .HasMaxLength(IdempotencyRecord.LocationMaxLength);
        builder.Property(record => record.CreatedAt).HasDefaultValueSql("now()");

        builder
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(record => record.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
