using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class RewardsModel : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_PointsLedgerEntries_Type",
                table: "PointsLedgerEntries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_FuelPurchases_Amount_Range",
                table: "FuelPurchases"
            );

            // EF guessed Amount -> PaidAmount; the existing column is the receipt value, i.e. GrossAmount.
            migrationBuilder.RenameColumn(
                name: "Amount",
                table: "FuelPurchases",
                newName: "GrossAmount"
            );

            // Existing purchases had no coupon: Discount 0, PaidAmount = GrossAmount (points unchanged).
            migrationBuilder.AddColumn<decimal>(
                name: "Discount",
                table: "FuelPurchases",
                type: "numeric(10,2)",
                precision: 10,
                scale: 2,
                nullable: false,
                defaultValue: 0m
            );
            migrationBuilder.Sql(
                "ALTER TABLE \"FuelPurchases\" ALTER COLUMN \"Discount\" DROP DEFAULT;"
            );

            migrationBuilder.Sql(
                """
                ALTER TABLE "FuelPurchases" ADD COLUMN "PaidAmount" numeric(10,2);
                UPDATE "FuelPurchases" SET "PaidAmount" = "GrossAmount" - "Discount";
                ALTER TABLE "FuelPurchases" ALTER COLUMN "PaidAmount" SET NOT NULL;
                """
            );

            migrationBuilder.CreateTable(
                name: "RewardOffers",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    Description = table.Column<string>(
                        type: "character varying(500)",
                        maxLength: 500,
                        nullable: false
                    ),
                    PointsCost = table.Column<int>(type: "integer", nullable: false),
                    DiscountPerLiter = table.Column<decimal>(
                        type: "numeric(4,2)",
                        precision: 4,
                        scale: 2,
                        nullable: false
                    ),
                    MaxLiters = table.Column<decimal>(
                        type: "numeric(7,3)",
                        precision: 7,
                        scale: 3,
                        nullable: false
                    ),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    UpdatedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    xmin = table.Column<uint>(type: "xid", rowVersion: true, nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RewardOffers", x => x.Id);
                    table.CheckConstraint(
                        "CK_RewardOffers_DiscountPerLiter_Range",
                        "\"DiscountPerLiter\" > 0 AND \"DiscountPerLiter\" <= 10"
                    );
                    table.CheckConstraint(
                        "CK_RewardOffers_MaxLiters_Range",
                        "\"MaxLiters\" > 0 AND \"MaxLiters\" <= 1000"
                    );
                    table.CheckConstraint("CK_RewardOffers_Name_NotBlank", "btrim(\"Name\") <> ''");
                    table.CheckConstraint(
                        "CK_RewardOffers_PointsCost_Range",
                        "\"PointsCost\" >= 1 AND \"PointsCost\" <= 1000000"
                    );
                }
            );

            migrationBuilder.CreateTable(
                name: "Coupons",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    UserId = table.Column<string>(type: "text", nullable: false),
                    OfferId = table.Column<Guid>(type: "uuid", nullable: false),
                    OfferName = table.Column<string>(
                        type: "character varying(100)",
                        maxLength: 100,
                        nullable: false
                    ),
                    PointsCost = table.Column<int>(type: "integer", nullable: false),
                    DiscountPerLiter = table.Column<decimal>(
                        type: "numeric(4,2)",
                        precision: 4,
                        scale: 2,
                        nullable: false
                    ),
                    MaxLiters = table.Column<decimal>(
                        type: "numeric(7,3)",
                        precision: 7,
                        scale: 3,
                        nullable: false
                    ),
                    Status = table.Column<string>(
                        type: "character varying(16)",
                        maxLength: 16,
                        nullable: false
                    ),
                    Code = table.Column<string>(
                        type: "character(27)",
                        fixedLength: true,
                        maxLength: 27,
                        nullable: true
                    ),
                    PurchasedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: false,
                        defaultValueSql: "now()"
                    ),
                    ActivatedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    RedeemedAt = table.Column<DateTimeOffset>(
                        type: "timestamp with time zone",
                        nullable: true
                    ),
                    RedeemedPurchaseId = table.Column<Guid>(type: "uuid", nullable: true),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coupons", x => x.Id);
                    table.CheckConstraint(
                        "CK_Coupons_Code_Format",
                        "\"Code\" IS NULL OR \"Code\" ~ '^K[0-7][0-9A-HJKMNP-TV-Z]{25}$'"
                    );
                    table.CheckConstraint(
                        "CK_Coupons_DiscountPerLiter_Range",
                        "\"DiscountPerLiter\" > 0 AND \"DiscountPerLiter\" <= 10"
                    );
                    table.CheckConstraint(
                        "CK_Coupons_MaxLiters_Range",
                        "\"MaxLiters\" > 0 AND \"MaxLiters\" <= 1000"
                    );
                    table.CheckConstraint(
                        "CK_Coupons_PointsCost_Range",
                        "\"PointsCost\" >= 1 AND \"PointsCost\" <= 1000000"
                    );
                    table.CheckConstraint(
                        "CK_Coupons_State",
                        "(\"Status\" = 'Purchased' AND \"Code\" IS NULL AND \"ActivatedAt\" IS NULL AND \"RedeemedAt\" IS NULL AND \"RedeemedPurchaseId\" IS NULL)\nOR (\"Status\" = 'Active' AND \"Code\" IS NOT NULL AND \"ActivatedAt\" IS NOT NULL AND \"RedeemedAt\" IS NULL AND \"RedeemedPurchaseId\" IS NULL)\nOR (\"Status\" = 'Redeemed' AND \"Code\" IS NOT NULL AND \"ActivatedAt\" IS NOT NULL AND \"RedeemedAt\" IS NOT NULL AND \"RedeemedPurchaseId\" IS NOT NULL)"
                    );
                    table.CheckConstraint(
                        "CK_Coupons_Status",
                        "\"Status\" IN ('Purchased', 'Active', 'Redeemed')"
                    );
                    table.CheckConstraint(
                        "CK_Coupons_Timeline",
                        "(\"ActivatedAt\" IS NULL OR \"ActivatedAt\" >= \"PurchasedAt\") AND (\"RedeemedAt\" IS NULL OR \"RedeemedAt\" >= \"ActivatedAt\")"
                    );
                    table.ForeignKey(
                        name: "FK_Coupons_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_Coupons_FuelPurchases_RedeemedPurchaseId",
                        column: x => x.RedeemedPurchaseId,
                        principalTable: "FuelPurchases",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                    table.ForeignKey(
                        name: "FK_Coupons_RewardOffers_OfferId",
                        column: x => x.OfferId,
                        principalTable: "RewardOffers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict
                    );
                }
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_PointsLedgerEntries_Delta_SignByType",
                table: "PointsLedgerEntries",
                sql: "(\"Type\" = 'FuelPurchase' AND \"Delta\" > 0) OR (\"Type\" = 'CouponExchange' AND \"Delta\" < 0)"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_PointsLedgerEntries_Type",
                table: "PointsLedgerEntries",
                sql: "\"Type\" IN ('FuelPurchase', 'CouponExchange')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FuelPurchases_Discount_Range",
                table: "FuelPurchases",
                sql: "\"Discount\" >= 0 AND \"Discount\" <= \"GrossAmount\""
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FuelPurchases_GrossAmount_Range",
                table: "FuelPurchases",
                sql: "\"GrossAmount\" > 0 AND \"GrossAmount\" <= 5000"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FuelPurchases_PaidAmount",
                table: "FuelPurchases",
                sql: "\"PaidAmount\" = \"GrossAmount\" - \"Discount\""
            );

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_OfferId",
                table: "Coupons",
                column: "OfferId"
            );

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_UserId_Status",
                table: "Coupons",
                columns: new[] { "UserId", "Status" }
            );

            migrationBuilder.CreateIndex(
                name: "UX_Coupons_Code",
                table: "Coupons",
                column: "Code",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "UX_Coupons_RedeemedPurchase",
                table: "Coupons",
                column: "RedeemedPurchaseId",
                unique: true
            );

            migrationBuilder.CreateIndex(
                name: "IX_RewardOffers_IsActive",
                table: "RewardOffers",
                column: "IsActive"
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(name: "Coupons");

            migrationBuilder.DropTable(name: "RewardOffers");

            migrationBuilder.DropCheckConstraint(
                name: "CK_PointsLedgerEntries_Delta_SignByType",
                table: "PointsLedgerEntries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_PointsLedgerEntries_Type",
                table: "PointsLedgerEntries"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_FuelPurchases_Discount_Range",
                table: "FuelPurchases"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_FuelPurchases_GrossAmount_Range",
                table: "FuelPurchases"
            );

            migrationBuilder.DropCheckConstraint(
                name: "CK_FuelPurchases_PaidAmount",
                table: "FuelPurchases"
            );

            migrationBuilder.DropColumn(name: "Discount", table: "FuelPurchases");

            migrationBuilder.DropColumn(name: "PaidAmount", table: "FuelPurchases");

            migrationBuilder.RenameColumn(
                name: "GrossAmount",
                table: "FuelPurchases",
                newName: "Amount"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_PointsLedgerEntries_Type",
                table: "PointsLedgerEntries",
                sql: "\"Type\" IN ('FuelPurchase')"
            );

            migrationBuilder.AddCheckConstraint(
                name: "CK_FuelPurchases_Amount_Range",
                table: "FuelPurchases",
                sql: "\"Amount\" > 0 AND \"Amount\" <= 5000"
            );
        }
    }
}
