using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelClub.Api.Modules.Loyalty.Infrastructure;

internal sealed class LoyaltyAccountConfiguration : IEntityTypeConfiguration<LoyaltyAccount>
{
    public void Configure(EntityTypeBuilder<LoyaltyAccount> builder)
    {
        builder.ToTable(
            "LoyaltyAccounts",
            table =>
                table.HasCheckConstraint(
                    "CK_LoyaltyAccounts_Balance_NonNegative",
                    "\"Balance\" >= 0"
                )
        );

        builder.HasKey(account => account.UserId);
        builder.Property(account => account.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(account => account.Version).IsRowVersion();

        builder
            .HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<LoyaltyAccount>(account => account.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class FuelPurchaseConfiguration : IEntityTypeConfiguration<FuelPurchase>
{
    private static string Sql(decimal value) =>
        value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public void Configure(EntityTypeBuilder<FuelPurchase> builder)
    {
        builder.ToTable(
            "FuelPurchases",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_FuelPurchases_GrossAmount_Range",
                    $"\"GrossAmount\" > 0 AND \"GrossAmount\" <= {Sql(FuelPurchaseRules.MaxAmount)}"
                );
                table.HasCheckConstraint(
                    "CK_FuelPurchases_Discount_Range",
                    "\"Discount\" >= 0 AND \"Discount\" <= \"GrossAmount\""
                );
                table.HasCheckConstraint(
                    "CK_FuelPurchases_PaidAmount",
                    "\"PaidAmount\" = \"GrossAmount\" - \"Discount\""
                );
                table.HasCheckConstraint(
                    "CK_FuelPurchases_Liters_Range",
                    $"\"Liters\" > 0 AND \"Liters\" <= {Sql(FuelPurchaseRules.MaxLiters)}"
                );
                table.HasCheckConstraint(
                    "CK_FuelPurchases_PointsAwarded_NonNegative",
                    "\"PointsAwarded\" >= 0"
                );
                table.HasCheckConstraint(
                    "CK_FuelPurchases_SaleDate_Set",
                    "\"SaleDate\" > DATE '0001-01-01'"
                );
                // Only the normalized form (trimmed, upper-case, allowed characters) may be stored,
                // so whitespace or letter case cannot bypass the receipt uniqueness.
                table.HasCheckConstraint(
                    "CK_FuelPurchases_StationCode_Format",
                    $"\"StationCode\" ~ '{FuelPurchaseRules.CodePattern}'"
                );
                table.HasCheckConstraint(
                    "CK_FuelPurchases_RegisterCode_Format",
                    $"\"RegisterCode\" ~ '{FuelPurchaseRules.CodePattern}'"
                );
                table.HasCheckConstraint(
                    "CK_FuelPurchases_ReceiptNumber_Format",
                    $"\"ReceiptNumber\" ~ '{FuelPurchaseRules.ReceiptNumberPattern}'"
                );
            }
        );

        builder.HasKey(purchase => purchase.Id);
        builder.Property(purchase => purchase.Id).ValueGeneratedNever();
        builder
            .Property(purchase => purchase.StationCode)
            .HasMaxLength(FuelPurchaseRules.CodeMaxLength);
        builder
            .Property(purchase => purchase.RegisterCode)
            .HasMaxLength(FuelPurchaseRules.CodeMaxLength);
        builder
            .Property(purchase => purchase.ReceiptNumber)
            .HasMaxLength(FuelPurchaseRules.ReceiptNumberMaxLength);
        builder
            .Property(purchase => purchase.GrossAmount)
            .HasPrecision(FuelPurchaseRules.AmountPrecision, FuelPurchaseRules.AmountScale);
        builder
            .Property(purchase => purchase.Discount)
            .HasPrecision(FuelPurchaseRules.AmountPrecision, FuelPurchaseRules.AmountScale);
        builder
            .Property(purchase => purchase.PaidAmount)
            .HasPrecision(FuelPurchaseRules.AmountPrecision, FuelPurchaseRules.AmountScale);
        builder
            .Property(purchase => purchase.Liters)
            .HasPrecision(FuelPurchaseRules.LitersPrecision, FuelPurchaseRules.LitersScale);
        builder.Property(purchase => purchase.CreatedAt).HasDefaultValueSql("now()");

        builder
            .HasIndex(purchase => new
            {
                purchase.StationCode,
                purchase.RegisterCode,
                purchase.SaleDate,
                purchase.ReceiptNumber,
            })
            .IsUnique()
            .HasDatabaseName("UX_FuelPurchases_Receipt");
        builder.HasIndex(purchase => purchase.UserId);

        builder
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(purchase => purchase.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(purchase => purchase.RegisteredByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class LoyaltyCardConfiguration : IEntityTypeConfiguration<LoyaltyCard>
{
    public void Configure(EntityTypeBuilder<LoyaltyCard> builder)
    {
        builder.ToTable(
            "LoyaltyCards",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_LoyaltyCards_Code_Format",
                    $"\"Code\" ~ '{LoyaltyCardCode.Pattern}'"
                );
                table.HasCheckConstraint(
                    "CK_LoyaltyCards_RevokedAt_AfterIssue",
                    "\"RevokedAt\" IS NULL OR \"RevokedAt\" >= \"IssuedAt\""
                );
            }
        );

        builder.HasKey(card => card.Id);
        builder.Property(card => card.Code).HasMaxLength(LoyaltyCardCode.Length).IsFixedLength();
        // Codes are never reused, including revoked ones.
        builder.HasIndex(card => card.Code).IsUnique().HasDatabaseName("UX_LoyaltyCards_Code");
        builder
            .HasIndex(card => card.UserId)
            .IsUnique()
            .HasFilter("\"RevokedAt\" IS NULL")
            .HasDatabaseName("UX_LoyaltyCards_ActivePerUser");

        builder
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(card => card.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class CashierStationConfiguration : IEntityTypeConfiguration<CashierStation>
{
    public void Configure(EntityTypeBuilder<CashierStation> builder)
    {
        builder.ToTable(
            "CashierStations",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_CashierStations_StationCode_Format",
                    $"\"StationCode\" ~ '{FuelPurchaseRules.CodePattern}'"
                );
                table.HasCheckConstraint(
                    "CK_CashierStations_RegisterCode_Format",
                    $"\"RegisterCode\" ~ '{FuelPurchaseRules.CodePattern}'"
                );
            }
        );

        builder.HasKey(station => station.UserId);
        builder
            .Property(station => station.StationCode)
            .HasMaxLength(FuelPurchaseRules.CodeMaxLength);
        builder
            .Property(station => station.RegisterCode)
            .HasMaxLength(FuelPurchaseRules.CodeMaxLength);

        builder
            .HasOne<ApplicationUser>()
            .WithOne()
            .HasForeignKey<CashierStation>(station => station.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class PointsLedgerEntryConfiguration : IEntityTypeConfiguration<PointsLedgerEntry>
{
    public void Configure(EntityTypeBuilder<PointsLedgerEntry> builder)
    {
        builder.ToTable(
            "PointsLedgerEntries",
            table =>
            {
                table.HasCheckConstraint("CK_PointsLedgerEntries_Delta_NonZero", "\"Delta\" <> 0");
                table.HasCheckConstraint(
                    "CK_PointsLedgerEntries_BalanceAfter_NonNegative",
                    "\"BalanceAfter\" >= 0"
                );
                table.HasCheckConstraint(
                    "CK_PointsLedgerEntries_Type",
                    "\"Type\" IN ('FuelPurchase', 'CouponExchange')"
                );
                // Purchases only credit points, exchanges only debit them.
                table.HasCheckConstraint(
                    "CK_PointsLedgerEntries_Delta_SignByType",
                    "(\"Type\" = 'FuelPurchase' AND \"Delta\" > 0) OR (\"Type\" = 'CouponExchange' AND \"Delta\" < 0)"
                );
            }
        );

        builder.HasKey(entry => entry.Id);
        builder.Property(entry => entry.Id).ValueGeneratedNever();
        builder.Property(entry => entry.Type).HasConversion<string>().HasMaxLength(32);
        builder.Property(entry => entry.CreatedAt).HasDefaultValueSql("now()");

        builder
            .HasIndex(entry => new { entry.Type, entry.SourceId })
            .IsUnique()
            .HasDatabaseName("UX_PointsLedgerEntries_Source");
        builder.Property(entry => entry.Sequence).UseIdentityAlwaysColumn();
        builder
            .HasIndex(entry => entry.Sequence)
            .IsUnique()
            .HasDatabaseName("UX_PointsLedgerEntries_Sequence");
        builder.HasIndex(entry => new { entry.UserId, entry.Sequence });

        builder
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(entry => entry.UserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
