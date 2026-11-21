using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace FuelClub.Api.Migrations
{
    /// <inheritdoc />
    public partial class LoyaltyReceiptNormalization : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FuelPurchases_Liters_Positive",
                table: "FuelPurchases");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FuelPurchases_Liters_Range",
                table: "FuelPurchases",
                sql: "\"Liters\" > 0 AND \"Liters\" <= 9999999.999");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FuelPurchases_ReceiptNumber_Format",
                table: "FuelPurchases",
                sql: "\"ReceiptNumber\" ~ '^[A-Z0-9/-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FuelPurchases_RegisterCode_Format",
                table: "FuelPurchases",
                sql: "\"RegisterCode\" ~ '^[A-Z0-9-]+$'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FuelPurchases_SaleDate_Set",
                table: "FuelPurchases",
                sql: "\"SaleDate\" > DATE '0001-01-01'");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FuelPurchases_StationCode_Format",
                table: "FuelPurchases",
                sql: "\"StationCode\" ~ '^[A-Z0-9-]+$'");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropCheckConstraint(
                name: "CK_FuelPurchases_Liters_Range",
                table: "FuelPurchases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FuelPurchases_ReceiptNumber_Format",
                table: "FuelPurchases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FuelPurchases_RegisterCode_Format",
                table: "FuelPurchases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FuelPurchases_SaleDate_Set",
                table: "FuelPurchases");

            migrationBuilder.DropCheckConstraint(
                name: "CK_FuelPurchases_StationCode_Format",
                table: "FuelPurchases");

            migrationBuilder.AddCheckConstraint(
                name: "CK_FuelPurchases_Liters_Positive",
                table: "FuelPurchases",
                sql: "\"Liters\" > 0");
        }
    }
}
