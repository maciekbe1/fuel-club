using System.Globalization;
using FuelClub.Api.Modules.Accounts;
using FuelClub.Api.Modules.Loyalty.Domain;
using FuelClub.Api.Modules.Rewards.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace FuelClub.Api.Modules.Rewards.Infrastructure;

internal static class RewardsSql
{
    public static string Num(decimal value) => value.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// Benefit range shared by the offer and the coupon snapshot. A fuel discount has both terms, a
    /// product has neither (ranges below pass on NULL).
    /// </summary>
    public static void BenefitChecks(TableBuilder table, string name)
    {
        table.HasCheckConstraint($"CK_{name}_Kind", "\"Kind\" IN ('FuelDiscount', 'Product')");
        table.HasCheckConstraint(
            $"CK_{name}_KindTerms",
            """
            ("Kind" = 'FuelDiscount' AND "DiscountPerLiter" IS NOT NULL AND "MaxLiters" IS NOT NULL)
            OR ("Kind" = 'Product' AND "DiscountPerLiter" IS NULL AND "MaxLiters" IS NULL)
            """
        );
        table.HasCheckConstraint(
            $"CK_{name}_DiscountPerLiter_Range",
            $"\"DiscountPerLiter\" > 0 AND \"DiscountPerLiter\" <= {Num(RewardOfferRules.MaxDiscountPerLiter)}"
        );
        table.HasCheckConstraint(
            $"CK_{name}_MaxLiters_Range",
            $"\"MaxLiters\" > 0 AND \"MaxLiters\" <= {Num(RewardOfferRules.MaxDiscountLiters)}"
        );
        table.HasCheckConstraint(
            $"CK_{name}_PointsCost_Range",
            $"\"PointsCost\" >= 1 AND \"PointsCost\" <= {RewardOfferRules.MaxPointsCost}"
        );
    }
}

internal sealed class RewardOfferConfiguration : IEntityTypeConfiguration<RewardOffer>
{
    public void Configure(EntityTypeBuilder<RewardOffer> builder)
    {
        builder.ToTable(
            "RewardOffers",
            table =>
            {
                RewardsSql.BenefitChecks(table, "RewardOffers");
                table.HasCheckConstraint("CK_RewardOffers_Name_NotBlank", "btrim(\"Name\") <> ''");
                table.HasCheckConstraint("CK_RewardOffers_Version_Positive", "\"Version\" >= 1");
            }
        );

        builder.HasKey(offer => offer.Id);
        builder.Property(offer => offer.Id).ValueGeneratedNever();
        builder.Property(offer => offer.Name).HasMaxLength(RewardOfferRules.NameMaxLength);
        builder
            .Property(offer => offer.Description)
            .HasMaxLength(RewardOfferRules.DescriptionMaxLength);
        builder
            .Property(offer => offer.DiscountPerLiter)
            .HasPrecision(
                RewardOfferRules.DiscountPerLiterPrecision,
                RewardOfferRules.DiscountPerLiterScale
            );
        builder
            .Property(offer => offer.MaxLiters)
            .HasPrecision(RewardOfferRules.MaxLitersPrecision, RewardOfferRules.MaxLitersScale);
        builder.Property(offer => offer.Kind).HasConversion<string>().HasMaxLength(16);
        // Target of the coupon's (OfferId, Kind) key: a coupon's kind is its offer's kind.
        builder.HasAlternateKey(offer => new { offer.Id, offer.Kind });
        builder.Property(offer => offer.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(offer => offer.UpdatedAt).HasDefaultValueSql("now()");
        // EF updates compare the original version and AppDbContext raises it (see BumpRewardOfferVersions).
        builder.Property(offer => offer.Version).IsConcurrencyToken();
        builder.HasIndex(offer => offer.IsActive);
    }
}

internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public const string RedeemedPurchaseOwnerConstraint =
        "FK_Coupons_FuelPurchases_RedeemedPurchaseOwner";

    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable(
            "Coupons",
            table =>
            {
                RewardsSql.BenefitChecks(table, "Coupons");
                table.HasCheckConstraint(
                    "CK_Coupons_Status",
                    "\"Status\" IN ('Purchased', 'Active', 'Redeemed')"
                );
                table.HasCheckConstraint(
                    "CK_Coupons_Code_Format",
                    $"\"Code\" IS NULL OR \"Code\" ~ '{CouponCode.Pattern}'"
                );
                // Each status has exactly the fields it implies: no code before activation, no
                // redemption link before redemption, and nothing is ever "un-done". A redeemed fuel
                // discount points at a purchase and never a handover; a redeemed product the reverse.
                table.HasCheckConstraint(
                    "CK_Coupons_State",
                    """
                    ("Status" = 'Purchased' AND "Code" IS NULL AND "ActivatedAt" IS NULL AND "RedeemedAt" IS NULL AND "RedeemedPurchaseId" IS NULL AND "RedeemedHandoverId" IS NULL)
                    OR ("Status" = 'Active' AND "Code" IS NOT NULL AND "ActivatedAt" IS NOT NULL AND "RedeemedAt" IS NULL AND "RedeemedPurchaseId" IS NULL AND "RedeemedHandoverId" IS NULL)
                    OR ("Status" = 'Redeemed' AND "Code" IS NOT NULL AND "ActivatedAt" IS NOT NULL AND "RedeemedAt" IS NOT NULL
                        AND (("Kind" = 'FuelDiscount' AND "RedeemedPurchaseId" IS NOT NULL AND "RedeemedHandoverId" IS NULL)
                          OR ("Kind" = 'Product' AND "RedeemedPurchaseId" IS NULL AND "RedeemedHandoverId" IS NOT NULL)))
                    """
                );
                table.HasCheckConstraint(
                    "CK_Coupons_Timeline",
                    "(\"ActivatedAt\" IS NULL OR \"ActivatedAt\" >= \"PurchasedAt\") AND (\"RedeemedAt\" IS NULL OR \"RedeemedAt\" >= \"ActivatedAt\")"
                );
            }
        );

        builder.HasKey(coupon => coupon.Id);
        builder.Property(coupon => coupon.Id).ValueGeneratedNever();
        builder.Property(coupon => coupon.OfferName).HasMaxLength(RewardOfferRules.NameMaxLength);
        builder
            .Property(coupon => coupon.DiscountPerLiter)
            .HasPrecision(
                RewardOfferRules.DiscountPerLiterPrecision,
                RewardOfferRules.DiscountPerLiterScale
            );
        builder
            .Property(coupon => coupon.MaxLiters)
            .HasPrecision(RewardOfferRules.MaxLitersPrecision, RewardOfferRules.MaxLitersScale);
        builder.Property(coupon => coupon.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(coupon => coupon.Kind).HasConversion<string>().HasMaxLength(16);
        builder.Property(coupon => coupon.Code).HasMaxLength(CouponCode.Length).IsFixedLength();
        builder.Property(coupon => coupon.PurchasedAt).HasDefaultValueSql("now()");

        builder.HasIndex(coupon => coupon.Code).IsUnique().HasDatabaseName("UX_Coupons_Code");
        builder
            .HasIndex(coupon => coupon.RedeemedPurchaseId)
            .IsUnique()
            .HasDatabaseName("UX_Coupons_RedeemedPurchase");
        builder.HasIndex(coupon => new { coupon.UserId, coupon.Status });

        builder
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(coupon => coupon.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<RewardOffer>()
            .WithMany()
            .HasForeignKey(coupon => new { coupon.OfferId, coupon.Kind })
            .HasPrincipalKey(offer => new { offer.Id, offer.Kind })
            .HasConstraintName("FK_Coupons_RewardOffers_OfferKind")
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasIndex(coupon => coupon.RedeemedHandoverId)
            .IsUnique()
            .HasDatabaseName("UX_Coupons_RedeemedHandover");
        // Composite: the handover a coupon points at is this coupon's, for this coupon's owner.
        builder
            .HasOne<RewardHandover>()
            .WithMany()
            .HasForeignKey(coupon => new
            {
                coupon.RedeemedHandoverId,
                coupon.Id,
                coupon.UserId,
            })
            .HasPrincipalKey(handover => new
            {
                handover.Id,
                handover.CouponId,
                handover.UserId,
            })
            .HasConstraintName("FK_Coupons_RewardHandovers_Handover")
            .OnDelete(DeleteBehavior.Restrict);
        // Composite: a coupon can only be redeemed on a purchase of its own owner.
        builder
            .HasOne<FuelPurchase>()
            .WithMany()
            .HasForeignKey(coupon => new { coupon.RedeemedPurchaseId, coupon.UserId })
            .HasPrincipalKey(purchase => new { purchase.Id, purchase.UserId })
            .HasConstraintName(RedeemedPurchaseOwnerConstraint)
            .OnDelete(DeleteBehavior.Restrict);
    }
}

internal sealed class RewardHandoverConfiguration : IEntityTypeConfiguration<RewardHandover>
{
    public void Configure(EntityTypeBuilder<RewardHandover> builder)
    {
        builder.ToTable(
            "RewardHandovers",
            table =>
            {
                table.HasCheckConstraint(
                    "CK_RewardHandovers_StationCode_Format",
                    $"\"StationCode\" ~ '{FuelPurchaseRules.CodePattern}'"
                );
                table.HasCheckConstraint(
                    "CK_RewardHandovers_RegisterCode_Format",
                    $"\"RegisterCode\" ~ '{FuelPurchaseRules.CodePattern}'"
                );
            }
        );
        builder.HasKey(handover => handover.Id);
        builder.Property(handover => handover.Id).ValueGeneratedNever();
        builder
            .Property(handover => handover.StationCode)
            .HasMaxLength(FuelPurchaseRules.CodeMaxLength);
        builder
            .Property(handover => handover.RegisterCode)
            .HasMaxLength(FuelPurchaseRules.CodeMaxLength);
        builder.Property(handover => handover.HandedOverAt).HasDefaultValueSql("clock_timestamp()");
        builder
            .HasIndex(handover => handover.CouponId)
            .IsUnique()
            .HasDatabaseName("UX_RewardHandovers_Coupon");
        // Target of the coupon's key, which ties coupon, handover and owner together. No key back
        // to the coupon: the two would form a cycle that no row could be deleted from.
        builder.HasAlternateKey(handover => new
        {
            handover.Id,
            handover.CouponId,
            handover.UserId,
        });
        builder
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(handover => handover.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder
            .HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(handover => handover.CashierUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
