using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class ProductRewards : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Coupons_RewardOffers_OfferId",
                table: "Coupons"
            );

            migrationBuilder.DropIndex(name: "IX_Coupons_OfferId", table: "Coupons");

            migrationBuilder.DropCheckConstraint(name: "CK_Coupons_State", table: "Coupons");

            migrationBuilder.AlterColumn<decimal>(
                name: "MaxLiters",
                table: "RewardOffers",
                type: "numeric(7,3)",
                precision: 7,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(7,3)",
                oldPrecision: 7,
                oldScale: 3
            );

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountPerLiter",
                table: "RewardOffers",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,2)",
                oldPrecision: 4,
                oldScale: 2
            );

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "RewardOffers",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "FuelDiscount"
            );

            migrationBuilder.AlterColumn<decimal>(
                name: "MaxLiters",
                table: "Coupons",
                type: "numeric(7,3)",
                precision: 7,
                scale: 3,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(7,3)",
                oldPrecision: 7,
                oldScale: 3
            );

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountPerLiter",
                table: "Coupons",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: true,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,2)",
                oldPrecision: 4,
                oldScale: 2
            );

            migrationBuilder.AddColumn<string>(
                name: "Kind",
                table: "Coupons",
                type: "character varying(16)",
                maxLength: 16,
                nullable: false,
                defaultValue: "FuelDiscount"
            );

            migrationBuilder.AddColumn<Guid>(
                name: "RedeemedHandoverId",
                table: "Coupons",
                type: "uuid",
                nullable: true
            );

            migrationBuilder.AddUniqueConstraint(
                name: "AK_RewardOffers_Id_Kind",
                table: "RewardOffers",
                columns: new[] { "Id", "Kind" }
            );

            migrationBuilder.CreateTable(
                name: "RewardHandovers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    CouponId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    CashierUserId = table.Column<string>(type: "text", nullable: false),
                    StationCode = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    RegisterCode = table.Column<string>(
                        type: "character varying(32)",
                        maxLength: 32,
                        nullable: false
                    ),
                    HandedOverAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "clock_timestamp()"
                    ),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardHandovers", x => x.Id);
                    table.UniqueConstraint(
                        "AK_RewardHandovers_Id_CouponId_UserId",
                        x => new
                        {
                            x.Id,
                            x.CouponId,
                            x.UserId,
                        }
                    );
                    table.CheckConstraint(
                        "CK_RewardHandovers_RegisterCode_Format",
                        "\"RegisterCode\" ~ '^[A-Z0-9-]+$'"
                    );
                    table.CheckConstraint(
                        "CK_RewardHandovers_StationCode_Format",
                        "\"StationCode\" ~ '^[A-Z0-9-]+$'"
                    );
                    table.ForeignKey(
                        name: "FK_RewardHandovers_AspNetUsers_CashierUserId",
                        column: x => x.CashierUserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_RewardHandovers_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_RewardOffers_Kind",
                table: "RewardOffers",
                sql: "\"Kind\" IN ('FuelDiscount', 'Product')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_RewardOffers_KindTerms",
                table: "RewardOffers",
                sql: "(\"Kind\" = 'FuelDiscount' AND \"DiscountPerLiter\" IS NOT NULL AND \"MaxLiters\" IS NOT NULL)\nOR (\"Kind\" = 'Product' AND \"DiscountPerLiter\" IS NULL AND \"MaxLiters\" IS NULL)"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_OfferId_Kind",
                table: "Coupons",
                columns: new[] { "OfferId", "Kind" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_RedeemedHandoverId_Id_UserId",
                table: "Coupons",
                columns: new[] { "RedeemedHandoverId", "Id", "UserId" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_Coupons_RedeemedHandover",
                table: "Coupons",
                column: "RedeemedHandoverId",
                unique: true
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_Kind",
                table: "Coupons",
                sql: "\"Kind\" IN ('FuelDiscount', 'Product')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_KindTerms",
                table: "Coupons",
                sql: "(\"Kind\" = 'FuelDiscount' AND \"DiscountPerLiter\" IS NOT NULL AND \"MaxLiters\" IS NOT NULL)\nOR (\"Kind\" = 'Product' AND \"DiscountPerLiter\" IS NULL AND \"MaxLiters\" IS NULL)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_State",
                table: "Coupons",
                sql: "(\"Status\" = 'Purchased' AND \"Code\" IS NULL AND \"ActivatedAt\" IS NULL AND \"RedeemedAt\" IS NULL AND \"RedeemedPurchaseId\" IS NULL AND \"RedeemedHandoverId\" IS NULL)\nOR (\"Status\" = 'Active' AND \"Code\" IS NOT NULL AND \"ActivatedAt\" IS NOT NULL AND \"RedeemedAt\" IS NULL AND \"RedeemedPurchaseId\" IS NULL AND \"RedeemedHandoverId\" IS NULL)\nOR (\"Status\" = 'Redeemed' AND \"Code\" IS NOT NULL AND \"ActivatedAt\" IS NOT NULL AND \"RedeemedAt\" IS NOT NULL\n    AND ((\"Kind\" = 'FuelDiscount' AND \"RedeemedPurchaseId\" IS NOT NULL AND \"RedeemedHandoverId\" IS NULL)\n      OR (\"Kind\" = 'Product' AND \"RedeemedPurchaseId\" IS NULL AND \"RedeemedHandoverId\" IS NOT NULL)))"
            );

            migrationBuilder.CreateIndex(
                name: "IX_RewardHandovers_CashierUserId",
                table: "RewardHandovers",
                column: "CashierUserId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_RewardHandovers_UserId",
                table: "RewardHandovers",
                column: "UserId"
            );

            migrationBuilder.CreateIndex(
                name: "UX_RewardHandovers_Coupon",
                table: "RewardHandovers",
                column: "CouponId",
                unique: true
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Coupons_RewardHandovers_Handover",
                table: "Coupons",
                columns: new[] { "RedeemedHandoverId", "Id", "UserId" },
                principalTable: "RewardHandovers",
                principalColumns: new[] { "Id", "CouponId", "UserId" },
                onDelete: ReferentialAction.Restrict
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Coupons_RewardOffers_OfferKind",
                table: "Coupons",
                columns: new[] { "OfferId", "Kind" },
                principalTable: "RewardOffers",
                principalColumns: new[] { "Id", "Kind" },
                onDelete: ReferentialAction.Restrict
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Product rewards have no per-liter terms, so the old schema cannot hold them: stop
            // instead of deleting customers' rewards.
            migrationBuilder.Sql(
                """
                DO $$
                BEGIN
                    IF EXISTS (SELECT 1 FROM "RewardOffers" WHERE "Kind" = 'Product') THEN
                        RAISE EXCEPTION 'Product rewards exist; remove them before rolling back ProductRewards';
                    END IF;
                END $$;
                """
            );

            migrationBuilder.DropForeignKey(
                name: "FK_Coupons_RewardHandovers_Handover",
                table: "Coupons"
            );

            migrationBuilder.DropForeignKey(
                name: "FK_Coupons_RewardOffers_OfferKind",
                table: "Coupons"
            );

            migrationBuilder.DropTable(name: "RewardHandovers");

            migrationBuilder.DropUniqueConstraint(
                name: "AK_RewardOffers_Id_Kind",
                table: "RewardOffers"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_RewardOffers_Kind",
                table: "RewardOffers"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_RewardOffers_KindTerms",
                table: "RewardOffers"
            );

            migrationBuilder.DropIndex(name: "IX_Coupons_OfferId_Kind", table: "Coupons");

            migrationBuilder.DropIndex(
                name: "IX_Coupons_RedeemedHandoverId_Id_UserId",
                table: "Coupons"
            );

            migrationBuilder.DropIndex(name: "UX_Coupons_RedeemedHandover", table: "Coupons");

            migrationBuilder.DropCheckConstraint(name: "CK_Coupons_Kind", table: "Coupons");

            migrationBuilder.DropCheckConstraint(name: "CK_Coupons_KindTerms", table: "Coupons");

            migrationBuilder.DropCheckConstraint(name: "CK_Coupons_State", table: "Coupons");

            migrationBuilder.DropColumn(name: "Kind", table: "RewardOffers");

            migrationBuilder.DropColumn(name: "Kind", table: "Coupons");

            migrationBuilder.DropColumn(name: "RedeemedHandoverId", table: "Coupons");

            migrationBuilder.AlterColumn<decimal>(
                name: "MaxLiters",
                table: "RewardOffers",
                type: "numeric(7,3)",
                precision: 7,
                scale: 3,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(7,3)",
                oldPrecision: 7,
                oldScale: 3,
                oldNullable: true
            );

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountPerLiter",
                table: "RewardOffers",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,2)",
                oldPrecision: 4,
                oldScale: 2,
                oldNullable: true
            );

            migrationBuilder.AlterColumn<decimal>(
                name: "MaxLiters",
                table: "Coupons",
                type: "numeric(7,3)",
                precision: 7,
                scale: 3,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(7,3)",
                oldPrecision: 7,
                oldScale: 3,
                oldNullable: true
            );

            migrationBuilder.AlterColumn<decimal>(
                name: "DiscountPerLiter",
                table: "Coupons",
                type: "numeric(4,2)",
                precision: 4,
                scale: 2,
                nullable: false,
                defaultValue: 0m,
                oldClrType: typeof(decimal),
                oldType: "numeric(4,2)",
                oldPrecision: 4,
                oldScale: 2,
                oldNullable: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_OfferId",
                table: "Coupons",
                column: "OfferId"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_Coupons_State",
                table: "Coupons",
                sql: "(\"Status\" = 'Purchased' AND \"Code\" IS NULL AND \"ActivatedAt\" IS NULL AND \"RedeemedAt\" IS NULL AND \"RedeemedPurchaseId\" IS NULL)\nOR (\"Status\" = 'Active' AND \"Code\" IS NOT NULL AND \"ActivatedAt\" IS NOT NULL AND \"RedeemedAt\" IS NULL AND \"RedeemedPurchaseId\" IS NULL)\nOR (\"Status\" = 'Redeemed' AND \"Code\" IS NOT NULL AND \"ActivatedAt\" IS NOT NULL AND \"RedeemedAt\" IS NOT NULL AND \"RedeemedPurchaseId\" IS NOT NULL)"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Coupons_RewardOffers_OfferId",
                table: "Coupons",
                column: "OfferId",
                principalTable: "RewardOffers",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict
            );
        }
    }
}
