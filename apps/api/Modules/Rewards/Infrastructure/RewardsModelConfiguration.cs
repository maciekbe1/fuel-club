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

    /// <summary>Benefit range shared by the offer and the coupon snapshot.</summary>
    public static void BenefitChecks(TableBuilder table, string name)
    {
        table.HasCheckConstraint(
            $"CK_{name}_DiscountPerLiter_Range",
            $"\"DiscountPerLiter\" > 0 AND \"DiscountPerLiter\" <= {Num(RewardOfferRules.MaxDiscountPerLiter)}");
        table.HasCheckConstraint(
            $"CK_{name}_MaxLiters_Range",
            $"\"MaxLiters\" > 0 AND \"MaxLiters\" <= {Num(RewardOfferRules.MaxDiscountLiters)}");
        table.HasCheckConstraint(
            $"CK_{name}_PointsCost_Range",
            $"\"PointsCost\" >= 1 AND \"PointsCost\" <= {RewardOfferRules.MaxPointsCost}");
    }
}

internal sealed class RewardOfferConfiguration : IEntityTypeConfiguration<RewardOffer>
{
    public void Configure(EntityTypeBuilder<RewardOffer> builder)
    {
        builder.ToTable("RewardOffers", table =>
        {
            RewardsSql.BenefitChecks(table, "RewardOffers");
            table.HasCheckConstraint("CK_RewardOffers_Name_NotBlank", "btrim(\"Name\") <> ''");
            table.HasCheckConstraint("CK_RewardOffers_Version_Positive", "\"Version\" >= 1");
        });

        builder.HasKey(offer => offer.Id);
        builder.Property(offer => offer.Id).ValueGeneratedNever();
        builder.Property(offer => offer.Name).HasMaxLength(RewardOfferRules.NameMaxLength);
        builder.Property(offer => offer.Description).HasMaxLength(RewardOfferRules.DescriptionMaxLength);
        builder.Property(offer => offer.DiscountPerLiter)
            .HasPrecision(RewardOfferRules.DiscountPerLiterPrecision, RewardOfferRules.DiscountPerLiterScale);
        builder.Property(offer => offer.MaxLiters)
            .HasPrecision(RewardOfferRules.MaxLitersPrecision, RewardOfferRules.MaxLitersScale);
        builder.Property(offer => offer.CreatedAt).HasDefaultValueSql("now()");
        builder.Property(offer => offer.UpdatedAt).HasDefaultValueSql("now()");
        // EF updates compare the original version and AppDbContext raises it (see BumpRewardOfferVersions).
        builder.Property(offer => offer.Version).IsConcurrencyToken();
        builder.HasIndex(offer => offer.IsActive);
    }
}

internal sealed class CouponConfiguration : IEntityTypeConfiguration<Coupon>
{
    public void Configure(EntityTypeBuilder<Coupon> builder)
    {
        builder.ToTable("Coupons", table =>
        {
            RewardsSql.BenefitChecks(table, "Coupons");
            table.HasCheckConstraint("CK_Coupons_Status", "\"Status\" IN ('Purchased', 'Active', 'Redeemed')");
            table.HasCheckConstraint("CK_Coupons_Code_Format", $"\"Code\" IS NULL OR \"Code\" ~ '{CouponCode.Pattern}'");
            // Each status has exactly the fields it implies: no code before activation, no purchase
            // link before redemption, and nothing is ever "un-done".
            table.HasCheckConstraint(
                "CK_Coupons_State",
                """
                ("Status" = 'Purchased' AND "Code" IS NULL AND "ActivatedAt" IS NULL AND "RedeemedAt" IS NULL AND "RedeemedPurchaseId" IS NULL)
                OR ("Status" = 'Active' AND "Code" IS NOT NULL AND "ActivatedAt" IS NOT NULL AND "RedeemedAt" IS NULL AND "RedeemedPurchaseId" IS NULL)
                OR ("Status" = 'Redeemed' AND "Code" IS NOT NULL AND "ActivatedAt" IS NOT NULL AND "RedeemedAt" IS NOT NULL AND "RedeemedPurchaseId" IS NOT NULL)
                """);
            table.HasCheckConstraint(
                "CK_Coupons_Timeline",
                "(\"ActivatedAt\" IS NULL OR \"ActivatedAt\" >= \"PurchasedAt\") AND (\"RedeemedAt\" IS NULL OR \"RedeemedAt\" >= \"ActivatedAt\")");
        });

        builder.HasKey(coupon => coupon.Id);
        builder.Property(coupon => coupon.Id).ValueGeneratedNever();
        builder.Property(coupon => coupon.OfferName).HasMaxLength(RewardOfferRules.NameMaxLength);
        builder.Property(coupon => coupon.DiscountPerLiter)
            .HasPrecision(RewardOfferRules.DiscountPerLiterPrecision, RewardOfferRules.DiscountPerLiterScale);
        builder.Property(coupon => coupon.MaxLiters)
            .HasPrecision(RewardOfferRules.MaxLitersPrecision, RewardOfferRules.MaxLitersScale);
        builder.Property(coupon => coupon.Status).HasConversion<string>().HasMaxLength(16);
        builder.Property(coupon => coupon.Code).HasMaxLength(CouponCode.Length).IsFixedLength();
        builder.Property(coupon => coupon.PurchasedAt).HasDefaultValueSql("now()");

        builder.HasIndex(coupon => coupon.Code).IsUnique().HasDatabaseName("UX_Coupons_Code");
        builder.HasIndex(coupon => coupon.RedeemedPurchaseId).IsUnique().HasDatabaseName("UX_Coupons_RedeemedPurchase");
        builder.HasIndex(coupon => new { coupon.UserId, coupon.Status });

        builder.HasOne<ApplicationUser>()
            .WithMany()
            .HasForeignKey(coupon => coupon.UserId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<RewardOffer>()
            .WithMany()
            .HasForeignKey(coupon => coupon.OfferId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<FuelPurchase>()
            .WithMany()
            .HasForeignKey(coupon => coupon.RedeemedPurchaseId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
