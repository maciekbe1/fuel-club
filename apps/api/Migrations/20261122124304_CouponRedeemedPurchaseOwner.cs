using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class CouponRedeemedPurchaseOwner : Migration
    {
        /// <summary>Fails with a count and a query listing the rows; public so a test can run it.</summary>
        public const string OwnerCheckSql = """
            DO $$
            DECLARE mismatched integer;
            BEGIN
                SELECT count(*) INTO mismatched
                FROM "Coupons" c JOIN "FuelPurchases" p ON p."Id" = c."RedeemedPurchaseId"
                WHERE p."UserId" <> c."UserId";
                IF mismatched > 0 THEN
                    RAISE EXCEPTION 'CouponRedeemedPurchaseOwner: % coupon(s) redeemed on another customer''s purchase. Fix them manually, then rerun. List: SELECT c."Id" FROM "Coupons" c JOIN "FuelPurchases" p ON p."Id" = c."RedeemedPurchaseId" WHERE p."UserId" <> c."UserId"', mismatched;
                END IF;
            END $$;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Detects existing inconsistencies and stops; data is never rewritten automatically.
            migrationBuilder.Sql(OwnerCheckSql);

            migrationBuilder.DropForeignKey(
                name: "FK_Coupons_FuelPurchases_RedeemedPurchaseId",
                table: "Coupons"
            );

            migrationBuilder.AddUniqueConstraint(
                name: "AK_FuelPurchases_Id_UserId",
                table: "FuelPurchases",
                columns: new[] { "Id", "UserId" }
            );

            migrationBuilder.CreateIndex(
                name: "IX_Coupons_RedeemedPurchaseId_UserId",
                table: "Coupons",
                columns: new[] { "RedeemedPurchaseId", "UserId" }
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Coupons_FuelPurchases_RedeemedPurchaseOwner",
                table: "Coupons",
                columns: new[] { "RedeemedPurchaseId", "UserId" },
                principalTable: "FuelPurchases",
                principalColumns: new[] { "Id", "UserId" },
                onDelete: ReferentialAction.Restrict
            );
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Coupons_FuelPurchases_RedeemedPurchaseOwner",
                table: "Coupons"
            );

            migrationBuilder.DropUniqueConstraint(
                name: "AK_FuelPurchases_Id_UserId",
                table: "FuelPurchases"
            );

            migrationBuilder.DropIndex(
                name: "IX_Coupons_RedeemedPurchaseId_UserId",
                table: "Coupons"
            );

            migrationBuilder.AddForeignKey(
                name: "FK_Coupons_FuelPurchases_RedeemedPurchaseId",
                table: "Coupons",
                column: "RedeemedPurchaseId",
                principalTable: "FuelPurchases",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict
            );
        }
    }
}
